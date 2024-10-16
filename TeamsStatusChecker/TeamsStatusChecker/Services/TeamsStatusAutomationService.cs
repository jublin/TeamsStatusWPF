﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using System.Windows.Automation;
using TeamsStatusChecker.Configuration;

namespace TeamsStatusChecker.Services
{
    public class TeamsStatusAutomationService : IMicrosoftTeamsService
    {
        public bool isChecking { get; private set; }
        private AutomationElement storedTeamsWindow = null;
        private readonly CancellationToken token = new CancellationToken();
        private readonly Serilog.ILogger _logger;
        private readonly SourceWindowsAutomationConfiguration _windowsAutomationConfiguration;
        private MicrosoftTeamsStatus _lastStatus = MicrosoftTeamsStatus.Unknown;

        public EventHandler<MicrosoftTeamsStatus> StatusChanged;

        public TeamsStatusAutomationService(
            SourceWindowsAutomationConfiguration config, Serilog.ILogger logger)
        {
            _logger = logger;

            if (config == null)
                throw new ConfigurationException("Configuration not found for source service WindowsAutomationConfiguration.");

            _windowsAutomationConfiguration = config;
            PoolingInterval = _windowsAutomationConfiguration.Interval;
        }

        public int PoolingInterval { get; set; }

        public async Task<MicrosoftTeamsStatus> GetCurrentStatus()
        {
            // return Task.FromResult(MicrosoftTeamsStatus.Busy);
            if (_windowsAutomationConfiguration == null)
            {
                return MicrosoftTeamsStatus.Unknown;
            }

            isChecking = true;
            var presenceStatus = "Unknown";
            var windowName = _windowsAutomationConfiguration.WindowName;
            var statusPattern = _windowsAutomationConfiguration.StatusPattern.Replace("@status", "(\\w+)");

            try
            {
                var rootElement = await Task.Run(() => AutomationElement.RootElement, token);

                // Check if we already have a valid storedTeamsWindow
                if (storedTeamsWindow != null)
                {
                    try
                    {
                        // Try to access a property to check if it's still valid
                        var cachedWindowName = storedTeamsWindow.Current.Name;
                    }
                    catch
                    {
                        // If accessing the property fails, the stored window is no longer valid
                        storedTeamsWindow = null;
                    }
                }

                if (storedTeamsWindow == null)
                {
                    AutomationElement teamsWindow = null;

                    // Find all windows
                    var windows = await Task.Run(() =>
                    {
                        var windowCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
                        return rootElement.FindAll(TreeScope.Children, windowCondition);
                    }, token);

                    // Iterate through all of the found Windows to find the one for MS Teams.
                    // Teams does NOT need to be the active window. It CAN be minimized to the system tray and it will still be found.
                    foreach (AutomationElement window in windows)
                    {
                        if (window.Current.Name.Contains(windowName))
                        {
                            // Store the window that belongs to Teams as teamsWindow
                            teamsWindow = window;
                            // Store the found Teams window AutomationElement
                            storedTeamsWindow = teamsWindow;
                            break;
                        }
                    }

                    if (teamsWindow == null)
                    {
                        isChecking = false;
                        return MicrosoftTeamsStatus.Unknown; // Return early if no Teams window is found
                    }
                }

                // Look for the presence status element within the Teams window
                var presenceElements = await Task.Run(() =>
                {
                    var presenceCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                    return storedTeamsWindow.FindAll(TreeScope.Descendants, presenceCondition);
                }, token);

                foreach (AutomationElement element in presenceElements)
                {
                    // On my system, with the "new" Teams UI installed, I had to look for the string:
                    // "Your profile picture with status displayed as"
                    // and then look at the next word, which is my current status.
                    _logger.Information($"presence element name found: {element.Current.Name}");
                    if (!string.IsNullOrEmpty(element.Current.Name))
                    {
                        var match = Regex.Match(element.Current.Name, statusPattern);
                        if (match.Success)
                        {
                            // Let's grab the status by looking at everything after "displayed as ", removing the trailing ".",
                            // and setting it to lowercase. I set it to lowercase because that is how I have my ESP32-C3
                            // set up to read the data that this C# app sends to it.
                            presenceStatus = match.Groups[1].Value;
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "WindowsAutomation - Error reading status");
            }
            finally
            {
                isChecking = false;
            }

            _logger.Debug($"WindowsAutomation status found: {presenceStatus}");

            var newStatus = _lastStatus;

            // Return what we found
            switch (presenceStatus)
            {
                case "Available":
                    newStatus = MicrosoftTeamsStatus.Available;
                    break;

                case "Busy":
                    newStatus = MicrosoftTeamsStatus.Busy;
                    break;

                case "Presenting":
                case "Do not disturb":
                    newStatus = MicrosoftTeamsStatus.DoNotDisturb;
                    break;

                default:
                    _logger.Warning($"WindowsAutomation availability unknown: {presenceStatus}");
                    newStatus = MicrosoftTeamsStatus.Unknown;
                    break;
            }

            if (newStatus != _lastStatus)
            {
                _lastStatus = newStatus;
                _logger.Information($"WindowsAutomation status set to {_lastStatus}");
            }
            StatusChanged?.Invoke(this, _lastStatus);
            return _lastStatus;
        }
    }
}
