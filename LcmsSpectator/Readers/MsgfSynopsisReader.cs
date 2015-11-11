﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LcmsSpectator.Readers
{
    using System.Windows;

    using InformedProteomics.Backend.Data.Sequence;

    using LcmsSpectator.Models;
    using LcmsSpectator.Readers.SequenceReaders;

    using MTDBFramework.IO;

    using PHRPReader;

    public class MsgfSynopsisReader : IIdFileReader
    {
        private string filePath;

        public MsgfSynopsisReader(string filePath)
        {
            this.filePath = filePath;
        }
        
        public IEnumerable<PrSm> Read(IEnumerable<string> modIgnoreList = null, IProgress<double> progress = null)
        {
            var oStartupOptions = new clsPHRPStartupOptions { LoadModsAndSeqInfo = true };
            var phrpReader = new clsPHRPReader(this.filePath, oStartupOptions);

            if (!string.IsNullOrEmpty(phrpReader.ErrorMessage))
            {
                throw new Exception(phrpReader.ErrorMessage);
            }

            var identifications = new List<PrSm>();
            while (phrpReader.MoveNext())
            {
                phrpReader.FinalizeCurrentPSM();
                var psm = phrpReader.CurrentPSM;
                var proteins = psm.Proteins;

                var parsedSequence = this.ParseSequence(psm.PeptideCleanSequence, psm.ModifiedResidues);
                foreach (var protein in proteins)
                {
                    identifications.Add(new PrSm
                    {
                        Heavy = false,
                        ProteinName = protein,
                        ProteinDesc = string.Empty,
                        Charge = psm.Charge,
                        Sequence = parsedSequence,
                        SequenceText = this.GetSequenceText(parsedSequence),
                        Scan = psm.ScanNumber,
                        Score = Convert.ToDouble(psm.MSGFSpecProb),
                        UseGolfScoring = true,
                        QValue = 0,
                    });
                }
            }

            return identifications;
        }

        public Task<IEnumerable<PrSm>> ReadAsync(IEnumerable<string> modIgnoreList = null, IProgress<double> progress = null)
        {
            return Task.Run(() => this.Read(modIgnoreList, progress));
        }

        /// <summary>
        /// Parse a CLEAN sequence (containing no pre/post residues or modifications).
        /// </summary>
        /// <param name="sequenceText">The clean sequence.</param>
        /// <param name="modInfo">The modification info for the sequence.</param>
        /// <returns>The parsed sequence.</returns>
        private Sequence ParseSequence(string sequenceText, List<clsAminoAcidModInfo> modInfo)
        {
            var sequenceReader = new SequenceReader();
            var sequence = sequenceReader.Read(sequenceText);
            foreach (var mod in modInfo)
            {
                if (!mod.AmbiguousMod)
                {
                    var def = mod.ModDefinition;
                    var location = mod.ResidueLocInPeptide - 1;
                    var aminoAcid = sequence[location];

                    var modification = new Modification(0, mod.ModDefinition.ModificationMass, mod.ModDefinition.MassCorrectionTag);
                    sequence[location] = new ModifiedAminoAcid(aminoAcid, modification);
                }
            }

            // Force it to recalculate mass now that the modifications have been added.
            sequence = new Sequence(sequence);

            return sequence;
        }

        private string GetSequenceText(Sequence sequence)
        {
            var stringBuilder = new StringBuilder();
            foreach (var aminoAcid in sequence)
            {
                stringBuilder.Append(aminoAcid.Residue);
                var modifiedAminoAcid = aminoAcid as ModifiedAminoAcid;
                if (modifiedAminoAcid != null)
                {
                    stringBuilder.AppendFormat("[{0}]", modifiedAminoAcid.Modification.Name);
                }
            }

            var result = stringBuilder.ToString();

            if (result.Length == 0)
            {
                Console.WriteLine();
            }

            return result;
        }
    }
}
