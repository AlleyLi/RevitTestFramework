﻿using System.Collections.Generic;
using System.Linq;
using Autodesk.RevitAddIns;

namespace RTF.Framework
{
    /// <summary>
    /// The RunnerSetupData class is used to convey
    /// required setup information to the Runner constructor.
    /// </summary>
    public class RunnerSetupData : IRunnerSetupData
    {
        public string WorkingDirectory { get; set; }
        public string AssemblyPath { get; set; }
        public string TestAssembly { get; set; }
        public string Results { get; set; }
        public string Fixture { get; set; }
        public string Category { get; set; }
        public string Test { get; set; }
        public bool Concat { get; set; }
        public bool DryRun { get; set; }
        public string RevitPath { get; set; }
        public bool CleanUp { get; set; }
        public bool Continuous { get; set; }
        public bool IsDebug { get; set; }
        public GroupingType GroupingType { get; set; }
        public IList<RevitProduct> Products { get; set; }
        public int Timeout { get; set; }
        public string ExcludedCategory { get; set; }
        public bool CopyAddins { get; set; }

        public RunnerSetupData()
        {
            Products = FindRevit();
            CleanUp = true;
            GroupingType = GroupingType.Fixture;
            Timeout = 120000;
        }

        public static IList<RevitProduct> FindRevit()
        {
            var products = RevitProductUtility.GetAllInstalledRevitProducts();

            if (products.Any())
            {
                products = products.Where(x => x.Version == RevitVersion.Revit2014).ToList();
            }

            return products;
        }
    }
}
