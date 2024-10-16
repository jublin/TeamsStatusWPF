﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsStatusChecker.Configuration
{
    public class SourceWindowsAutomationConfiguration
    {
        public int Interval { get; set; }
        public string? WindowName { get; set; }
        public string? StatusPattern { get; set; }
    }
}
