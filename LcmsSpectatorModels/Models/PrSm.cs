﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InformedProteomics.Backend.Data.Biology;
using InformedProteomics.Backend.Data.Sequence;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.MassSpecData;

namespace LcmsSpectatorModels.Models
{
    public class PrSm: IComparable<PrSm>
    {
        public LcMsRun Lcms { get; set; }
        public int Scan { get; set; }
        public Spectrum Ms2Spectrum { get { return (Lcms == null) ? null : Lcms.GetSpectrum(Scan); } }
        public string Protein { get; set; }
        public string Annotation { get; set; }
        public string SequenceText { get; set; }
        public List<string> SequenceLabel { get; set; }
        public Sequence Sequence { get; set; }
        public string Pre { get; set; }
        public string Post { get; set; }
        public List<Tuple<int, Modification>> Modifications { get; set; }
        public string Composition { get; set; }
        public string ProteinName { get; set; }
        public string ProteinDesc { get; set; }
        public string ProteinNameDesc { get; set; }
        public int ProteinLength { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int Charge { get; set; }
        public double MostAbundantIsotopeMz { get; set; }
        public double Mass { get; set; }
        public double MatchedFragments { get; set; }
        public double QValue { get; set; }
        public double PepQValue { get; set; }
        public static double CorrelationThreshold = 0.7;

        public Spectrum PreviousMs1 
        {
            get
            {
                var prevms1Scan = Lcms.GetPrevScanNum(Scan, 1);
                return Lcms.GetSpectrum(prevms1Scan);
            }
        }

        public Spectrum NextMs1
        {
            get
            {
                var nextms1Scan = Lcms.GetNextScanNum(Scan, 1);
                return Lcms.GetSpectrum(nextms1Scan);
            }
        }

        public double PrecursorMz
        {
            get
            {
                var composition = Sequence.Aggregate(InformedProteomics.Backend.Data.Composition.Composition.Zero, (current, aa) => current + aa.Composition);
                var ion = new Ion(composition + InformedProteomics.Backend.Data.Composition.Composition.H2O, Charge);
                return Math.Round(ion.GetMonoIsotopicMz(), 2);
            }
        }

        public int CompareTo(PrSm other)
        {
            return Scan.CompareTo(other.Scan);
        }

        public void SetModifications(string modifications)
        {
            Modifications = new List<Tuple<int, Modification>>();
            // Build Sequence AminoAcid list
            SequenceText = Protein;
            Sequence = new Sequence(SequenceText, new AminoAcidSet());
            foreach (var aa in Sequence) SequenceLabel.Add(aa.Residue.ToString(CultureInfo.InvariantCulture));
            ParseModifications(modifications);

            // Add modifications to sequence
            Modifications.Sort(new CompareModByHighestPosition());   // sort in reverse order for insertion
            foreach (var mod in Modifications)
            {
                var pos = mod.Item1;
                if (pos > 0) pos--;
                var modLabel = String.Format("[{0}]", mod.Item2.Name);
                SequenceText = SequenceText.Insert(mod.Item1, modLabel);
                SequenceLabel[pos] += modLabel;
                var aa = Sequence[pos];
                var modaa = new ModifiedAminoAcid(aa, mod.Item2);
                Sequence[pos] = modaa;
            }
        }

        private void ParseModifications(string modifications)
        {
            var mods = modifications.Split(',');
            if (mods.Length < 1 || mods[0] == "") return;
            foreach (var modParts in mods.Select(mod => mod.Split(' ')))
            {
                if (modParts.Length < 0) throw new FormatException("Invalid modification.");
                var modName = modParts[0];
                var modPos = Convert.ToInt32(modParts[1]);
                Modifications.Add(new Tuple<int, Modification>(modPos, Modification.Get(modName)));
            }
        }
    }

    public class PrSmScoreComparer : IComparer<PrSm>
    {
        public int Compare(PrSm x, PrSm y)
        {
            return (x.MatchedFragments.CompareTo(y.MatchedFragments));
        }
    }

    internal class CompareModByHighestPosition : IComparer<Tuple<int, Modification>>
    {
        public int Compare(Tuple<int, Modification> x, Tuple<int, Modification> y)
        {
            return (y.Item1.CompareTo(x.Item1));
        }
    }
}
