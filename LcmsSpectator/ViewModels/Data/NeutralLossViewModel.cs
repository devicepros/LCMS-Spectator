﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NeutralLossViewModel.cs" company="Pacific Northwest National Laboratory">
//   2015 Pacific Northwest National Laboratory
// </copyright>
// <author>Christopher Wilkins</author>
// <summary>
//   View model for a NeutralLoss model.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using InformedProteomics.Backend.Data.Spectrometry;
using ReactiveUI;

namespace LcmsSpectator.ViewModels.Data
{
    /// <summary>
    /// View model for a NeutralLoss model.
    /// </summary>
    public class NeutralLossViewModel : ReactiveObject
    {
        /// <summary>
        /// The neutral loss model for this view model.
        /// </summary>
        private NeutralLoss neutralLoss;

        /// <summary>
        /// A value indicating whether this neutral loss is selected.
        /// </summary>
        private bool isSelected;

        /// <summary>
        /// Gets or sets the neutral loss model for this view model.
        /// </summary>
        public NeutralLoss NeutralLoss
        {
            get => neutralLoss;
            set => this.RaiseAndSetIfChanged(ref neutralLoss, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this neutral loss is selected.
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set => this.RaiseAndSetIfChanged(ref isSelected, value);
        }
    }
}
