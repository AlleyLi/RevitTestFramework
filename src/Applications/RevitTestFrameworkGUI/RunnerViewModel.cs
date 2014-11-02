﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Xml.Serialization;
using Autodesk.RevitAddIns;
using Microsoft.Practices.Prism;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.ViewModel;
using RTF.Applications.Properties;
using RTF.Framework;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace RTF.Applications
{
    public interface IContext
    {
        bool IsSynchronized { get; }
        void Invoke(Action action);
        void BeginInvoke(Action action);
    }

    public sealed class WpfContext : IContext
    {
        private readonly Dispatcher _dispatcher;

        public bool IsSynchronized
        {
            get
            {
                return this._dispatcher.Thread == Thread.CurrentThread;
            }
        }

        public WpfContext()
            : this(Dispatcher.CurrentDispatcher)
        {
        }

        public WpfContext(Dispatcher dispatcher)
        {
            Debug.Assert(dispatcher != null);

            this._dispatcher = dispatcher;
        }

        public void Invoke(Action action)
        {
            Debug.Assert(action != null);

            this._dispatcher.Invoke(action);
        }

        public void BeginInvoke(Action action)
        {
            Debug.Assert(action != null);

            this._dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>
    /// The Runner's view model.
    /// </summary>
    public class RunnerViewModel : NotificationObject
    {
        #region private members

        private object selectedItem;
        private Runner runner;
        private bool isRunning = false;
        private object isRunningLock = new object();
        private IContext context;
        private FileSystemWatcher watcher;
        private string selectedTestSummary;
        private ObservableCollection<string> recentFiles = new ObservableCollection<string>();
 
        #endregion

        #region public properties

        public object SelectedItem
        {
            get { return selectedItem; }
            set
            {
                selectedItem = value;
                RaisePropertyChanged("SelectedItem");
                RaisePropertyChanged("RunText");
                RunCommand.RaiseCanExecuteChanged();
            }
        }

        public int SelectedProductIndex
        {
            get { return runner.SelectedProduct; }
            set
            {
                if (runner == null) return;

                runner.SelectedProduct = value;

                runner.RevitPath = runner.SelectedProduct == -1 ? 
                    string.Empty :
                    Path.Combine(runner.Products[value].InstallLocation, "revit.exe");

                RaisePropertyChanged("SelectedProductIndex");
            }
        }

        public string RunText
        {
            get
            {
                if (SelectedItem is IAssemblyData)
                {
                    return "Run All Tests in Selected Assembly";
                }
                
                if (SelectedItem is IFixtureData)
                {
                    return "Run All Tests in Selected Fixture";
                }
                 
                if(SelectedItem is ITestData)
                {
                    return "Run Selected Test";
                }

                if (SelectedItem is ICategoryData)
                {
                    return "Run All Tests in Selected Category";
                }

                return "Nothing Selected";
            }
            set
            {
                RaisePropertyChanged("RunText");
            }
        }
        
        public bool IsRunning
        {
            get
            {
                lock (isRunningLock)
                {
                    return isRunning;  
                }
            }
            set
            {
                lock (isRunningLock)
                {
                    isRunning = value;
                    RaisePropertyChanged("IsRunning");
                }
            }
        }

        public string SelectedTestSummary
        {
            get
            {
                if (runner == null) return string.Empty;

                var allTests = runner.GetAllTests();
                var selectedTests = runner.GetRunnableTests();

                return string.Format("{0} tests selected of {1}", selectedTests.Count(), allTests.Count());
            }
        }

        public ObservableCollection<string> RecentFiles
        {
            get { return recentFiles; }
            set
            {
                recentFiles = value;
                RaisePropertyChanged("RecentFiles");
                RaisePropertyChanged("HasRecentFiles");
            }
        }

        public bool HasRecentFiles
        {
            get { return recentFiles != null && recentFiles.Any(); }
        }

        public ObservableCollection<IAssemblyData> Assemblies
        {
            get { return runner.Assemblies; }
        }

        public ObservableCollection<RevitProduct> Products
        {
            get { return runner.Products; }
        }

        public bool IsDebug
        {
            get { return runner.IsDebug; }
            set
            {
                runner.IsDebug = value;
                RaisePropertyChanged("IsDebug");
            }
        }

        public bool Concat
        {
            get { return runner.Concat; }
            set
            {
                runner.Concat = value;
                RaisePropertyChanged("Concat");
            }
        }

        public int Timeout
        {
            get { return runner.Timeout; }
            set
            {
                runner.Timeout = value;
                RaisePropertyChanged("Timeout");
            }
        }

        public string WorkingDirectory
        {
            get { return runner.WorkingDirectory; }
            set
            {
                runner.WorkingDirectory = value;
                runner.InitializeTests();
                RaisePropertyChanged("WorkingDirectory");
                RaisePropertyChanged("Assemblies");
            }
        }

        public bool Continuous
        {
            get { return runner.Continuous; }
            set
            {
                runner.Continuous = value;
                RaisePropertyChanged("Continuous");
            }
        }

        public GroupingType GroupingType
        {
            get { return runner.GroupingType; }
            set
            {
                runner.GroupingType = value;
                runner.InitializeTests();
                RaisePropertyChanged("GroupingType");
                RaisePropertyChanged("Assemblies");
            }
        }

        public string TestAssembly
        {
            get { return runner.TestAssembly; }
            set
            {
                runner.TestAssembly = value;
                runner.InitializeTests();
                RaisePropertyChanged("TestAssembly");
                RaisePropertyChanged("Assemblies");
            }
        }

        public string Results
        {
            get { return runner.Results; }
            set
            {
                runner.Results = value;
                RaisePropertyChanged("Results");
            }
        }

        #endregion

        #region commands

        public DelegateCommand SetAssemblyPathCommand { get; set; }
        public DelegateCommand SetResultsPathCommand { get; set; }
        public DelegateCommand SetWorkingPathCommand { get; set; }
        public DelegateCommand<object> RunCommand { get; set; }
        public DelegateCommand CleanupCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }
        public DelegateCommand UpdateCommand { get; set; }
        public DelegateCommand OpenCommand { get; set; }
        public DelegateCommand SaveCommand { get; set; }
        public DelegateCommand<object> OpenFileCommand { get; set; }

        #endregion

        #region constructors

        internal RunnerViewModel(IContext context)
        {
            this.context = context;

            InitializeRecentFiles();

            InitializeRunner();

            InitializeCommands();

            InitializeFileWatcher();
        }

        private void InitializeRunner()
        {
            // If the recent files contains a previously
            // saved runner, then open the first saved file
            if (Settings.Default.recentFiles.Count > 0)
            {
                OpenFile(Settings.Default.recentFiles[0]);
            }
            else
            {
                // Create a default runner
                runner = new Runner();
            }

            if (runner.SelectedProduct > runner.Products.Count - 1)
            {
                SelectedProductIndex = -1;
            }
        }

        #endregion

        #region setup

        private void InitializeRecentFiles()
        {
            if (Settings.Default.recentFiles != null)
            {
                RecentFiles.AddRange(Settings.Default.recentFiles.Cast<string>());
            }
        }

        private void InitializeFileWatcher()
        {
            watcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(runner.AssemblyPath),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(runner.AssemblyPath)
            };
            watcher.Changed += watcher_Changed;
            watcher.EnableRaisingEvents = true;
        }

        private void InitializeEventHandlers()
        {
            runner.Assemblies.CollectionChanged += Assemblies_CollectionChanged;
            runner.Products.CollectionChanged += Products_CollectionChanged;
            runner.TestComplete += runner_TestComplete;
            runner.TestFailed += runner_TestFailed;
            runner.TestTimedOut += runner_TestTimedOut;
            runner.Initialized += runner_Initialized;
        }

        private void RemoveEventHandlers()
        {
            runner.Assemblies.CollectionChanged -= Assemblies_CollectionChanged;
            runner.Products.CollectionChanged -= Products_CollectionChanged;
            runner.TestComplete -= runner_TestComplete;
            runner.TestFailed -= runner_TestFailed;
            runner.TestTimedOut -= runner_TestTimedOut;
            runner.Initialized -= runner_Initialized;
        }

        private void InitializeCommands()
        {
            SetAssemblyPathCommand = new DelegateCommand(SetAssemblyPath, CanSetAssemblyPath);
            SetResultsPathCommand = new DelegateCommand(SetResultsPath, CanSetResultsPath);
            SetWorkingPathCommand = new DelegateCommand(SetWorkingDirectory, CanSetWorkingPath);
            RunCommand = new DelegateCommand<object>(Run, CanRun);
            CleanupCommand = new DelegateCommand(runner.Cleanup, CanCleanup);
            CancelCommand = new DelegateCommand(Cancel, CanCancel);
            UpdateCommand = new DelegateCommand(Update, CanUpdate);
            OpenCommand = new DelegateCommand(Open, CanOpen);
            SaveCommand = new DelegateCommand(Save, CanSave);
            OpenFileCommand = new DelegateCommand<object>(OpenFile, CanOpenFile);
        }

        private bool CanCleanup()
        {
            return true;
        }

        #endregion

        #region event handlers

        void runner_Initialized(object sender, EventArgs e)
        {
            RaisePropertyChanged("");
        }

        private void watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!isRunning)
            {
                context.BeginInvoke(() => runner.InitializeTests());
            }
        }

        private void runner_TestTimedOut(ITestData data)
        {
            context.BeginInvoke(() => Runner.Runner_TestTimedOut(data));
        }

        private void runner_TestFailed(ITestData data, string message, string stackTrace)
        {
            context.BeginInvoke(() => Runner.Runner_TestFailed(data, message, stackTrace));
        }

        private void runner_TestComplete(IEnumerable<ITestData> data, string resultsPath)
        {
            context.BeginInvoke(() => Runner.GetTestResultStatus(data, resultsPath));
        }

        void Products_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // When the products collection is changed, we want to set
            // the selected product index to the first in the list
            if (runner.Products.Count > 0)
            {
                SelectedProductIndex = 0;
            }
            else
            {
                SelectedProductIndex = -1;
            }

            RaisePropertyChanged("Products");
        }

        void Assemblies_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged("Assemblies");
        }

        #endregion

        #region private methods

        private bool CanRun(object parameter)
        {
            return runner.GetRunnableTests().Any();
        }

        private void Run(object parameter)
        {
            if (string.IsNullOrEmpty(runner.Results))
            {
                MessageBox.Show("Please select an output path for the results.");
                return;
            }

            if (File.Exists(runner.Results) && !runner.Concat)
            {
                File.Delete(runner.Results);
            }

            var worker = new BackgroundWorker();

            worker.DoWork += TestThread;
            worker.RunWorkerAsync(parameter);   
        }

        private void TestThread(object sender, DoWorkEventArgs e)
        {
            IsRunning = true;

            runner.SetupTests();
            runner.RunAllTests();

            IsRunning = false;
        }

        private bool CanSetWorkingPath()
        {
            return true;
        }

        private void SetWorkingDirectory()
        {
            var dirs = new FolderBrowserDialog();

            if (dirs.ShowDialog() == DialogResult.OK)
            {
                WorkingDirectory = dirs.SelectedPath;
            }
        }

        private bool CanSetResultsPath()
        {
            return true;
        }

        private void SetResultsPath()
        {
            var files = new SaveFileDialog()
            {
                InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Filter = "xml files (*.xml) | *.xml",
                RestoreDirectory = true,
                DefaultExt = ".xml"
            };

            var filesResult = files.ShowDialog();

            if (filesResult != null && filesResult == true)
            {
               Results = files.FileName;
               RunCommand.RaiseCanExecuteChanged();
            }
        }

        private bool CanSetAssemblyPath()
        {
            return true;
        }

        private void SetAssemblyPath()
        {
            var files = new OpenFileDialog
            {
                InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Filter = "assembly files (*.dll)|*.dll| executable files (*.exe)|*.exe",
                RestoreDirectory = true,
                DefaultExt = ".dll"
            };

            var filesResult = files.ShowDialog();

            if (filesResult != null && filesResult == true)
            {
                TestAssembly = files.FileName;
            }
        }

        private bool CanCancel()
        {
            return true;
        }

        private void Cancel()
        {
            if (runner != null && IsRunning)
            {
                runner.CancelRequested = true;
            }

            IsRunning = false;
        }

        private void Update()
        {
            RaisePropertyChanged("SelectedTestSummary");
        }

        private bool CanUpdate()
        {
            return true;
        }

        private bool CanSave()
        {
            return !isRunning;
        }

        private void Save()
        {
            var sfd = new SaveFileDialog
            {
                InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Filter = "xml files (*.xml)|*.xml",
                DefaultExt = ".xml",
                AddExtension = true,
                RestoreDirectory = true
            };

            // Call the ShowDialog method to show the dialog box.
            var ok = sfd.ShowDialog();

            // Process input if the user clicked OK.
            if (ok != true) return;

            Runner.Save(sfd.FileName, runner);

            SaveRecentFile(sfd.FileName);
        }

        private bool CanOpen()
        {
            return !isRunning;
        }

        private void Open()
        {
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Filter = "xml files (*.xml)|*.xml", 
                RestoreDirectory = true
            };

            // Call the ShowDialog method to show the dialog box.
            var ok = ofd.ShowDialog();

            // Process input if the user clicked OK.
            if (ok != true) return;

            //deserialize the settings
            runner = null;

            runner = Runner.Load(ofd.FileName);
            RaisePropertyChanged("");

            SaveRecentFile(ofd.FileName);
        }

        private void SaveRecentFile(string fileName)
        {
            RecentFiles.Clear();
            RecentFiles.Add(fileName);

            var i = 0;
            while (i < 2 && 
                Settings.Default.recentFiles != null &&
                Settings.Default.recentFiles.Count > i)
            {
                RecentFiles.Add(Settings.Default.recentFiles[i]);
                i++;
            }

            var sc = new StringCollection();
            sc.AddRange(RecentFiles.ToArray());
            Settings.Default.recentFiles = sc;
        }

        private bool CanOpenFile(object parameter)
        {
            return true;
        }

        private void OpenFile(object parameter)
        {
            if (!File.Exists(parameter.ToString()))
            {
                MessageBox.Show("The specified file no longer exists.");
                return;
            }

            // Clear the runner
            if (runner != null)
            {
                RemoveEventHandlers();
                runner = null;
            }

            runner = Runner.Load(parameter.ToString());

            InitializeEventHandlers();
        }

        #endregion
    }
}
