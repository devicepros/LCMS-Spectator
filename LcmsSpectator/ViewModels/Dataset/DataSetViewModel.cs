﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DatasetViewModel.cs" company="Pacific Northwest National Laboratory">
//   2015 Pacific Northwest National Laboratory
// </copyright>
// <author>Christopher Wilkins</author>
// <summary>
//   Class representing a data set, containing a LCMSRun, identifications, and features.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace LcmsSpectator.ViewModels.Dataset
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Threading.Tasks;
    using InformedProteomics.Backend.MassSpecData;
    using InformedProteomics.Backend.Utils;
    using LcmsSpectator.DialogServices;
    using LcmsSpectator.Models;
    using LcmsSpectator.Models.Dataset;
    using LcmsSpectator.Models.DTO;
    using LcmsSpectator.Readers;
    using LcmsSpectator.ViewModels.Data;
    using LcmsSpectator.ViewModels.Plots;
    using ReactiveUI;
    
    /// <summary>
    /// Class representing a data set, containing a LCMSRun, identifications, and features.
    /// </summary>
    public class DatasetViewModel : ReactiveObject
    {
        /// <summary>
        /// Dialog service for opening dialogs from view model.
        /// </summary>
        private readonly IMainDialogService dialogService;

        /// <summary>
        /// View model for extracted ion chromatogram
        /// </summary>
        private XicViewModel xicViewModel;

        /// <summary>
        /// View model for spectrum plots (MS/MS, previous MS1, next MS1) 
        /// </summary>
        private SpectrumViewModel spectrumViewModel;

        /// <summary>
        /// View model for mass spec feature map plot
        /// </summary>
        private FeatureViewerViewModel featureMapViewModel;

        /// <summary>
        /// A value indicating whether or not this data set is ready to be closed?
        /// </summary>
        private bool readyToClose;

        /// <summary>
        /// The selected Protein-Spectrum-Match Identification
        /// </summary>
        private PrSm selectedPrSm;

        /// <summary>
        /// A value indicating whether an ID file has been opened for this data set.
        /// </summary>
        private bool idFileOpen;

        /// <summary>
        /// A value indicating whether this dataset is loading.
        /// </summary>
        private bool isLoading;

        /// <summary>
        /// The progress of the loading.
        /// </summary>
        private double loadProgressPercent;

        /// <summary>
        /// The status message for the loading.
        /// </summary>
        private string loadProgressStatus;

        /// <summary>
        /// Initializes a new instance of the <see cref="DatasetViewModel"/> class. 
        /// </summary>
        /// <param name="datasetInfo">The dataset info model for this dataset.</param>
        /// <param name="dialogService">A dialog service for opening dialogs from the view model</param>
        public DatasetViewModel(DatasetInfo datasetInfo, IMainDialogService dialogService = null)
        {
            this.DatasetInfo = datasetInfo;
            this.dialogService = dialogService ?? new MainDialogService();
            this.ReadyToClose = false;
            this.IdFileOpen = false;
            this.SelectedPrSm = new PrSm();
            this.ScanViewModel = new ScanViewModel(dialogService, new List<PrSm>());
            this.CreateSequenceViewModel = new CreateSequenceViewModel(dialogService, this.LcMs)
            {
                ModificationSettings = datasetInfo.ModificationSettings,
            };

            this.CreateSequenceViewModel.CreateAndAddPrSmCommand.Subscribe(
                _ => this.ScanViewModel.Data.Add(this.CreateSequenceViewModel.SelectedPrSm));

            // Remove filter by raw file name from ScanViewModel filters
            this.ScanViewModel.Filters.Remove(this.ScanViewModel.Filters.FirstOrDefault(f => f.Name == "Raw File Name"));

            // When a PrSm is selected from the ScanViewModel, update the SelectedPrSm for this data set
            this.ScanViewModel.WhenAnyValue(x => x.SelectedPrSm).Where(prsm => prsm != null).Subscribe(x => this.SelectedPrSm = x);

            // When the scan number in the selected prsm changes, the selected scan in the xic plots should update
            this.WhenAnyValue(x => x.SelectedPrSm)
            .Where(_ => this.SelectedPrSm != null && this.SpectrumViewModel != null && this.XicViewModel != null)
            .Subscribe(prsm =>
            {
                this.SpectrumViewModel.UpdateSpectra(prsm.Scan, this.SelectedPrSm.PrecursorMz);
                this.XicViewModel.SetSelectedScan(prsm.Scan);
                this.XicViewModel.ZoomToScan(prsm.Scan);
            });

            var prsmObservable = this.WhenAnyValue(x => x.SelectedPrSm).Where(prsm => prsm != null);

            prsmObservable
                .Select(prsm => prsm.GetFragmentationSequence()).Where(fragSeq => fragSeq != null)
                .Subscribe(fragSeq =>
                    {
                        this.SpectrumViewModel.FragmentationSequence = fragSeq;
                        this.XicViewModel.FragmentationSequence = fragSeq;
                    });

            // When the prsm changes, update the Scan View Model.
            prsmObservable.Subscribe(prsm => this.ScanViewModel.SelectedPrSm = prsm);

            // When the prsm updates, update the prsm in the sequence creator
            prsmObservable.Subscribe(prsm => this.CreateSequenceViewModel.SelectedPrSm = prsm);

            // When the prsm updates, update the feature map
            prsmObservable.Where(_ => this.FeatureMapViewModel != null).Subscribe(prsm => this.FeatureMapViewModel.FeatureMapViewModel.SelectedPrSm = prsm);

            // When prsm updates, subscribe to scan updates
            prsmObservable.Subscribe(prsm =>
            {
                prsm.WhenAnyValue(x => x.Scan, x => x.PrecursorMz)
                    .Where(x => x.Item1 > 0 && x.Item2 > 0 && this.SpectrumViewModel != null)
                    .Subscribe(x => this.SpectrumViewModel.UpdateSpectra(x.Item1, x.Item2));
                prsm.WhenAnyValue(x => x.Scan).Where(scan => scan > 0 && this.XicViewModel != null)
                    .Subscribe(scan => this.XicViewModel.SetSelectedScan(scan));
            });

            // When a new prsm is created by CreateSequenceViewModel, update SelectedPrSm
            this.CreateSequenceViewModel.WhenAnyValue(x => x.SelectedPrSm).Subscribe(prsm => this.SelectedPrSm = prsm);

            // When IDs are filtered in the ScanViewModel, update feature map with new IDs
            this.ScanViewModel.WhenAnyValue(x => x.FilteredData).Where(_ => this.FeatureMapViewModel != null).Subscribe(data => this.FeatureMapViewModel.UpdateIds(data));

            //// Toggle instrument data when ShowInstrumentData setting is changed.
            //IcParameters.Instance.WhenAnyValue(x => x.ShowInstrumentData).Select(async x => await this.ScanViewModel.ToggleShowInstrumentDataAsync(x, (PbfLcMsRun)this.LcMs)).Subscribe();

            //// When product ion tolerance or ion correlation threshold change, update scorer factory
            //IcParameters.Instance.WhenAnyValue(x => x.ProductIonTolerancePpm, x => x.IonCorrelationThreshold)
            //    .Subscribe(
            //    x =>
            //    {
            //        var scorer = new ScorerFactory(x.Item1, Constants.MinCharge, Constants.MaxCharge, x.Item2);
            //        this.CreateSequenceViewModel.ScorerFactory = scorer;
            //        this.ScanViewModel.ScorerFactory = scorer;
            //    });

            // When an ID file has been opened, turn on the unidentified scan filter
            this.WhenAnyValue(x => x.IdFileOpen)
                .Where(idFileOpen => idFileOpen)
                .Select(_ => this.ScanViewModel.Filters.FirstOrDefault(f => f.Name == "Hide Unidentified Scans"))
                .Where(f => f != null)
                .Subscribe(f => f.Selected = true);

            // Start MsPf Search Command
            this.StartMsPfSearchCommand = ReactiveCommand.Create();
            this.StartMsPfSearchCommand.Subscribe(_ => this.StartMsPfSearchImplementation());

            // Close command verifies that the user wants to close the dataset, then sets ReadyToClose to true if they are
            this.CloseCommand = ReactiveCommand.Create();
            this.CloseCommand.Subscribe(_ =>
            {
                this.ReadyToClose =
                    this.dialogService.ConfirmationBox(
                        string.Format("Are you sure you would like to close {0}?", this.DatasetInfo.Name), string.Empty);
            });
        }

        /// <summary>
        /// Gets the LCMSRun representing the raw file for this dataset.
        /// </summary>
        public ILcMsRun LcMs { get; private set; }

        /// <summary>
        /// Gets ScanViewModel that contains a list of identifications for this data set.
        /// </summary>
        public ScanViewModel ScanViewModel { get; private set; }

        /// <summary>
        /// Gets the create sequence view model.
        /// </summary>
        public CreateSequenceViewModel CreateSequenceViewModel { get; private set; }

        /// <summary>
        /// Gets a command that starts an MSPathFinder with this data set.
        /// </summary>
        public ReactiveCommand<object> StartMsPfSearchCommand { get; private set; }

        /// <summary>
        /// Gets a command that is activated when the close button is clicked on a dataset.
        /// Initiates a close request for the main view model
        /// </summary>
        public ReactiveCommand<object> CloseCommand { get; private set; }

        /// <summary>
        /// Gets the dataset info for this dataset.
        /// </summary>
        public DatasetInfo DatasetInfo { get; private set; }

        /// <summary>
        /// Gets view model for extracted ion chromatogram
        /// </summary>
        public XicViewModel XicViewModel
        {
            get { return this.xicViewModel; }
            private set { this.RaiseAndSetIfChanged(ref this.xicViewModel, value); }
        }

        /// <summary>
        /// Gets view model for spectrum plots (MS/MS, previous MS1, next MS1) 
        /// </summary>
        public SpectrumViewModel SpectrumViewModel
        {
            get { return this.spectrumViewModel; }
            private set { this.RaiseAndSetIfChanged(ref this.spectrumViewModel, value); }
        }

        /// <summary>
        /// Gets view model for mass spec feature map plot
        /// </summary>
        public FeatureViewerViewModel FeatureMapViewModel
        {
            get { return this.featureMapViewModel; }
            private set { this.RaiseAndSetIfChanged(ref this.featureMapViewModel, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether or not this data set is ready to be closed?
        /// </summary>
        public bool ReadyToClose
        {
            get { return this.readyToClose; }
            set { this.RaiseAndSetIfChanged(ref this.readyToClose, value); }
        }

        /// <summary>
        /// Gets or sets the selected Protein-Spectrum-Match Identification
        /// </summary>
        public PrSm SelectedPrSm
        {
            get { return this.selectedPrSm; }
            set { this.RaiseAndSetIfChanged(ref this.selectedPrSm, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether an ID file has been opened for this data set.
        /// </summary>
        public bool IdFileOpen
        {
            get { return this.idFileOpen; }
            set { this.RaiseAndSetIfChanged(ref this.idFileOpen, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this dataset is loading.
        /// </summary>
        public bool IsLoading
        {
            get { return this.isLoading; }
            set { this.RaiseAndSetIfChanged(ref this.isLoading, value); }
        }

        /// <summary>
        /// Gets or sets the progress of the loading.
        /// </summary>
        public double LoadProgressPercent
        {
            get { return this.loadProgressPercent; }
            set { this.RaiseAndSetIfChanged(ref this.loadProgressPercent, value); }
        }

        /// <summary>
        /// Gets or sets the status message for the loading.
        /// </summary>
        public string LoadProgressStatus
        {
            get { return this.loadProgressStatus; }
            set { this.RaiseAndSetIfChanged(ref this.loadProgressStatus, value); }
        }

        /// <summary>
        /// Initialize this data set by reading the raw file asynchronously and initializing child view models
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task InitializeAsync()
        {
            this.IsLoading = true; // Show animated loading screen

            var progData = new ProgressData
            {
                UpdateFrequencySeconds = 1,
                IsPartialRange = true,
                MaxPercentage = 80
            };

            this.LoadProgressPercent = 0.0;
            this.LoadProgressStatus = "Loading...";
            var progress = new Progress<ProgressData>(pd =>
            {
                if (progData.ShouldUpdate())
                {
                    this.LoadProgressPercent = progData.UpdatePercent(pd.Percent).Percent;
                    this.LoadProgressStatus = pd.Status;   
                }
            });

            // Load data files.
            await this.LoadSpectrumFile(progress);

            progData.StepRange(90);
            await this.LoadIdFiles(progress);

            progData.StepRange(100);
            await Task.Run(() => this.LoadFeatureFiles(progress));

            // Now that we have a LcMsRun, initialize viewmodels that require it
            this.XicViewModel = new XicViewModel(this.dialogService, this.LcMs)
            {
                ToleranceSettings = this.DatasetInfo.ToleranceSettings,
                ImageExportSettings = this.DatasetInfo.ImageExportSettings
            };

            this.SpectrumViewModel = new SpectrumViewModel(this.dialogService, this.LcMs);
            this.FeatureMapViewModel = new FeatureViewerViewModel((LcMsRun)this.LcMs, this.dialogService);

            // When the selected scan changes in the xic plots, the selected scan for the prsm should update
            this.XicViewModel.SelectedScanUpdated().Subscribe(scan => this.SelectedPrSm.Scan = scan);

            // When an ID is selected on FeatureMap, update selectedPrSm
            this.FeatureMapViewModel.FeatureMapViewModel.WhenAnyValue(x => x.SelectedPrSm).Where(prsm => prsm != null).Subscribe(prsm => this.SelectedPrSm = prsm);

            // Create prsms for scan numbers (unidentified)
            this.SelectedPrSm.LcMs = this.LcMs; // For the selected PrSm, we should always use the LcMsRun for this dataset.

            this.IsLoading = false; // Hide animated loading screen
        }

        /// <summary>
        /// Load the spectrum file from spectrum file list.
        /// </summary>
        /// <param name="progress">The progress reporter.</param>
        /// <returns>The <see cref="Task" />.</returns>
        private async Task LoadSpectrumFile(IProgress<ProgressData> progress)
        {
            var progData = new ProgressData { IsPartialRange = true, MaxPercentage = 5 };
            var stepProgress = new Progress<ProgressData>(pd => progress.Report(progData.UpdatePercent(pd.Percent)));

            // load raw file
            var spectrumFilePath = this.DatasetInfo.GetSpectrumFilePath();
            string pbfPath, fileName, tempPath;
            if (!File.Exists(PbfLcMsRun.GetCheckPbfFilePath(spectrumFilePath, out pbfPath, out fileName, out tempPath)))
            {
                progData.MaxPercentage = 90;
            }

            this.LcMs = await Task.Run(() => PbfLcMsRun.GetLcMsRun(spectrumFilePath, 0, 0, stepProgress));

            // Load scans into scan data grid.
            progData.StepRange(100);
            progData.Status = "Loading scans.";
            await Task.Run(() => this.LoadScans(stepProgress));
        }

        /// <summary>
        /// Load all scans from a raw file and insert them into the scan view model.
        /// </summary>
        /// <param name="progress">The progress reporter.</param>
        private void LoadScans(IProgress<ProgressData> progress)
        {
            var progData = new ProgressData();
            var scans = this.LcMs.GetScanNumbers(1).ToList();
            scans.AddRange(this.LcMs.GetScanNumbers(2));
            scans.Sort();

            for (int i = 0; i < scans.Count; i++)
            {
                this.ScanViewModel.Data.Add(new PrSm
                {
                    Scan = scans[i],
                    RawFileName = this.DatasetInfo.Name,
                    LcMs = this.LcMs,
                    QValue = -1.0,
                    Score = double.NaN,
                });

                progress.Report(progData.UpdatePercent((100.0 * i) / scans.Count));
            }
        }

        /// <summary>
        /// Load all identification files.
        /// </summary>
        /// <param name="progress">The progress reporter.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task LoadIdFiles(IProgress<ProgressData> progress)
        {
            var progData = new ProgressData();
            var modIgnoreList = new List<string>();

            var idFiles =
                this.DatasetInfo.Files.Where(file => file.FileType == FileTypes.IdentificationFile).ToList();

            // Iterate over files to open
            for (int i = 0; i < idFiles.Count; i++)
            {
                progData.Status = string.Format("Loading {0}", Path.GetFileName(idFiles[i].FilePath));
                var reader = IdFileReaderFactory.CreateReader(idFiles[i].FilePath);
                bool attemptToReadFile = true;

                // If an InvalidModificationName exception is thrown, prompt the user to determine
                // how to resolve the modification. Keep trying to read the file until all modifications
                // are resolved.
                do
                {
                    try
                    {
                        var ids = await reader.ReadAsync();
                        var idList = ids.ToList();
                        foreach (var id in idList)
                        {
                            id.RawFileName = this.DatasetInfo.Name;
                            id.LcMs = this.LcMs;
                        }

                        this.ScanViewModel.Data.AddRange(idList);
                        this.IdFileOpen = true;
                        attemptToReadFile = false;
                    }
                    catch (IcFileReader.InvalidModificationNameException e)
                    {
                        // file contains an unknown modification
                        var result =
                            this.dialogService.ConfirmationBox(
                                string.Format(
                                    "{0}\nWould you like to add this modification?\nIf not, all sequences containing this modification will be ignored.",
                                    e.Message),
                                "Unknown Modification");
                        if ((!result) || SingletonProjectManager.Instance.PromptRegisterModification(e.ModificationName))
                        {
                            modIgnoreList.Add(e.ModificationName);
                        }
                    }
                    catch (KeyNotFoundException e)
                    {   // file does not have correct headers
                        this.dialogService.ExceptionAlert(e);
                        attemptToReadFile = false;
                    }
                    catch (IOException e)
                    {   // unable to read or open file.
                        this.dialogService.ExceptionAlert(e);
                        attemptToReadFile = false;
                    }
                }
                while (attemptToReadFile);

                progress.Report(progData.UpdatePercent((100.0 * i) / idFiles.Count));
            }
        }

        /// <summary>
        /// Load all feature files.
        /// </summary>
        /// <param name="progress">The progress reporter.</param>
        private void LoadFeatureFiles(IProgress<ProgressData> progress)
        {
            var progData = new ProgressData();
            var featureFilePaths =
                this.DatasetInfo.Files.Where(file => file.FileType == FileTypes.FeatureFile)
                    .Select(file => file.FilePath).ToList();

            for (int i = 0; i < featureFilePaths.Count; i++)
            {
                this.FeatureMapViewModel.OpenFeatureFile(featureFilePaths[i]);
                this.FeatureMapViewModel.UpdateIds(this.ScanViewModel.FilteredData);   

                progress.Report(progData.UpdatePercent((100.0 * i) / featureFilePaths.Count));
            }
        }

        /// <summary>
        /// Implementation for <see cref="StartMsPfSearchCommand" />.
        /// Gets a command that starts an MSPathFinder with this data set.
        /// </summary>
        private void StartMsPfSearchImplementation()
        {
            var searchSettings = new SearchSettingsViewModel(this.dialogService, this.DatasetInfo.Parameters)
            {
                SpectrumFilePath = this.DatasetInfo.GetSpectrumFilePath(),
                SelectedSearchMode = 1,
                FastaDbFilePath = this.DatasetInfo.Files.Where(file => file.FileType == FileTypes.FastaFile).Select(file => file.FilePath).FirstOrDefault(),
                OutputFilePath = string.Format("{0}\\{1}", Directory.GetCurrentDirectory(), this.DatasetInfo.Name),
                SelectedSequence = this.SelectedPrSm.Sequence.Aggregate(string.Empty, (current, aa) => current + aa.Residue),
                IonCorrelationThreshold = this.DatasetInfo.ToleranceSettings.IonCorrelationThreshold
            };

            // Set feature file path.
            if (this.FeatureMapViewModel != null)
            {
                searchSettings.FeatureFilePath = this.FeatureMapViewModel.FeatureFilePath;
            }

            // Select the correct protein
            if (searchSettings.FastaEntries.Count > 0)
            {
                foreach (var entry in searchSettings.FastaEntries)
                {
                    entry.Selected = entry.ProteinName == this.SelectedPrSm.ProteinName;
                }
            }

            // Set scan number of selected spectrum
            var scanNum = 0;
            if (this.SpectrumViewModel.Ms2SpectrumViewModel.Spectrum != null)
            {
                scanNum = this.SpectrumViewModel.Ms2SpectrumViewModel.Spectrum.ScanNum;
                searchSettings.MinScanNumber = scanNum;
                searchSettings.MaxScanNumber = scanNum;
            }

            // TODO: change this so it doesn't use an event and isn't void async
            searchSettings.ReadyToClose += async (o, e) =>
            {
                if (searchSettings.Status)
                {
                    var idFileReader = IdFileReaderFactory.CreateReader(searchSettings.GetIdFilePath());
                    var prsms = await idFileReader.ReadAsync();
                    var prsmList = prsms.ToList();
                    foreach (var prsm in prsmList)
                    {
                        prsm.LcMs = this.LcMs;
                    }

                    prsmList.Sort(new PrSm.PrSmScoreComparer());
                    this.ScanViewModel.Data.AddRange(prsmList);

                    var scanPrsms = prsmList.Where(prsm => prsm.Scan == scanNum).ToList();
                    if (scanNum > 0 && scanPrsms.Count > 0)
                    {
                        this.SelectedPrSm = scanPrsms[0];
                    }
                    else if (prsmList.Count > 0)
                    {
                        this.SelectedPrSm = prsmList[0];
                    }
                }
            };

            this.dialogService.SearchSettingsWindow(searchSettings);
        }
    }
}
