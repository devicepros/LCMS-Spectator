﻿namespace LcmsSpectator.ViewModels.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Threading.Tasks;

    using InformedProteomics.Backend.Data.Sequence;
    using InformedProteomics.Backend.Data.Spectrometry;

    using LcmsSpectator.Config;
    using LcmsSpectator.Models;
    using LcmsSpectator.Utils;
    using ReactiveUI;

    public class FragmentationSequenceViewModel : ReactiveObject, IFragmentationSequenceViewModel
    {
        /// <summary>
        /// The underlying fragmentation sequence.
        /// </summary>
        private FragmentationSequence fragmentationSequence;

        /// <summary>
        /// The heavy peptide modifications.
        /// </summary>
        private SearchModification[] heavyModifications;

        /// <summary>
        /// The LabeledIonViewModels for this sequence.
        /// </summary>
        private LabeledIonViewModel[] labeledIonViewModels;

        /// <summary>
        /// The selected ion types.
        /// </summary>
        private IonType[] selectedIonTypes;

        /// <summary>
        /// A value indicating whether to add precursor ions to labeled ion lists.
        /// </summary>
        private bool addPrecursorIons;

        /// <summary>
        /// Initializes a new instance of the <see cref="FragmentationSequenceViewModel"/> class.
        /// </summary>
        public FragmentationSequenceViewModel()
        {
            this.FragmentationSequence = new FragmentationSequence(
                new Sequence(new List<AminoAcid>()),
                1,
                null,
                ActivationMethod.HCD);

            this.BaseIonTypes = new ReactiveList<BaseIonTypeViewModel>
            {
                new BaseIonTypeViewModel { BaseIonType = BaseIonType.A },
                new BaseIonTypeViewModel { BaseIonType = BaseIonType.B, IsSelected = true },
                new BaseIonTypeViewModel { BaseIonType = BaseIonType.C },
                new BaseIonTypeViewModel { BaseIonType = BaseIonType.X },
                new BaseIonTypeViewModel { BaseIonType = BaseIonType.Y, IsSelected = true },
                new BaseIonTypeViewModel { BaseIonType = BaseIonType.Z }
            };

            this.BaseIonTypes.ChangeTrackingEnabled = true;

            this.NeutralLosses = new ReactiveList<NeutralLossViewModel>
            {
                new NeutralLossViewModel { NeutralLoss = NeutralLoss.NoLoss, IsSelected = true },
                new NeutralLossViewModel { NeutralLoss = NeutralLoss.H2O },
                new NeutralLossViewModel { NeutralLoss = NeutralLoss.NH3 }
            };

            this.NeutralLosses.ChangeTrackingEnabled = true;

            this.HeavyModifications = new SearchModification[0];
            this.LabeledIonViewModels = new LabeledIonViewModel[0];
            this.SelectedIonTypes = new IonType[0];

            this.AddPrecursorIons = true;

            // HideAllIonsCommand deselects all ion types and neutral losses.
            var hideAllIonsCommand = ReactiveCommand.Create();
            hideAllIonsCommand.Subscribe(_ =>
            {
                this.AddPrecursorIons = false;
                foreach (var baseIonType in BaseIonTypes)
                {
                    baseIonType.IsSelected = false;
                }

                foreach (var neutralLoss in NeutralLosses)
                {
                    neutralLoss.IsSelected = false;
                }
            });
            this.HideAllIonsCommand = hideAllIonsCommand;

            // When Base Ion Types are selected/deselected, update ion types.
            this.BaseIonTypes.ItemChanged.Where(x => x.PropertyName == "IsSelected")
                .Subscribe(_ => this.UpdateIonTypes());

            // When Neutral Losses are selected/deselected, update ion types
            this.NeutralLosses.ItemChanged.Where(x => x.PropertyName == "IsSelected")
                .Subscribe(_ => this.UpdateIonTypes());

            // When FragmentationSequence is set, select IonTypes for ActivationMethod.
            this.WhenAnyValue(x => x.FragmentationSequence)
                .Where(fragSeq => fragSeq != null)
                .Subscribe(fragSeq => this.SetActivationMethod(fragSeq.ActivationMethod));

            // When fragmentation sequence changes, update labeled ions
            this.WhenAnyValue(x => x.FragmentationSequence, x => x.SelectedIonTypes, x => x.HeavyModifications, x => x.AddPrecursorIons)
                .SelectMany(async _ => await this.GetLabeledIonViewModels())
                .Subscribe(livms => this.LabeledIonViewModels = livms);
        }

        /// <summary>
        /// Gets or sets the underlying fragmentation sequence.
        /// </summary>
        public FragmentationSequence FragmentationSequence
        {
            get { return this.fragmentationSequence; }
            set { this.RaiseAndSetIfChanged(ref this.fragmentationSequence, value); }
        }

        /// <summary>
        /// Gets a command that deselects all ion types.
        /// </summary>
        public IReactiveCommand HideAllIonsCommand { get; private set; }

        /// <summary>
        /// Gets the list of possible base ion types.
        /// </summary>
        public ReactiveList<BaseIonTypeViewModel> BaseIonTypes { get; private set; }
        
        /// <summary>
        /// Gets the list of possible neutral losses.
        /// </summary>
        public ReactiveList<NeutralLossViewModel> NeutralLosses { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether to add precursor ions to labeled ion lists.
        /// </summary>
        public bool AddPrecursorIons
        {
            get { return this.addPrecursorIons; }
            set { this.RaiseAndSetIfChanged(ref this.addPrecursorIons, value); }
        }

        /// <summary>
        /// Gets or sets the heavy peptide modifications.
        /// </summary>
        public SearchModification[] HeavyModifications
        {
            get { return this.heavyModifications; }
            set { this.RaiseAndSetIfChanged(ref this.heavyModifications, value); }
        }

        /// <summary>
        /// Gets the LabeledIonViewModels for this sequence.
        /// </summary>
        public LabeledIonViewModel[] LabeledIonViewModels
        {
            get { return this.labeledIonViewModels; }
            private set { this.RaiseAndSetIfChanged(ref this.labeledIonViewModels, value); }
        }

        /// <summary>
        /// Gets the selected ion types.
        /// </summary>
        public IonType[] SelectedIonTypes
        {
            get { return this.selectedIonTypes; }
            private set { this.RaiseAndSetIfChanged(ref this.selectedIonTypes, value); }
        }

        /// <summary>
        /// Gets the labeled ion view models for the sequence.
        /// </summary>
        /// <returns>Task that returns the labeled ion view models for the sequence.</returns>
        private async Task<LabeledIonViewModel[]> GetLabeledIonViewModels()
        {
            if (this.SelectedIonTypes == null)
            {
                return new LabeledIonViewModel[0];
            }

            var fragmentIons =
                await this.fragmentationSequence.GetFragmentLabelsAsync(this.SelectedIonTypes, this.HeavyModifications);

            if (this.AddPrecursorIons)
            {
                var precursorIons = await this.FragmentationSequence.GetChargePrecursorLabelsAsync(this.HeavyModifications);
                fragmentIons.AddRange(precursorIons);   
            }

            return fragmentIons.ToArray();
        }

        /// <summary>
        /// Set ion types based on the selected activation method.
        /// </summary>
        /// <param name="selectedActivationMethod">The selected activation method.</param>
        private void SetActivationMethod(ActivationMethod selectedActivationMethod)
        {
            if (!IcParameters.Instance.AutomaticallySelectIonTypes)
            {
                return;
            }

            var selectedBaseIonTypes = selectedActivationMethod == ActivationMethod.ETD
                                           ? IcParameters.Instance.EtdIonTypes
                                           : IcParameters.Instance.CidHcdIonTypes;

            foreach (var baseIonType in this.BaseIonTypes)
            {
                baseIonType.IsSelected = selectedBaseIonTypes.Contains(baseIonType.BaseIonType);
            }
        }

        /// <summary>
        /// Update SelectedIonTypes based on the selected BaseIonTypes and NeutralLosses.
        /// </summary>
        private void UpdateIonTypes()
        {
            this.SelectedIonTypes = IonUtils.GetIonTypes(
                IcParameters.Instance.IonTypeFactory,
                this.BaseIonTypes.Where(bit => bit.IsSelected).Select(bit => bit.BaseIonType).ToList(),
                this.NeutralLosses.Where(nl => nl.IsSelected).Select(nl => nl.NeutralLoss).ToList(),
                1,
                this.fragmentationSequence.Charge - 1).ToArray();
        }
    }
}
