using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace dpenner1.Chess.ChessDominationSolver
{
    public class SearchInfo
    {
        public string CurrentBoardRepresentation { get; set; }

        public int UpdateFrequencyMinutes { get; set; }

        public SearchSettings Settings { get; }
        public SearchStats Stats { get; }

        public SearchInfo(string searchData)
        {
            var parts = searchData.Split('|');
            CurrentBoardRepresentation = parts[0].Trim();

            var spaceParts = parts[2].Trim().Split('-');

            Settings = new SearchSettings(parts[1].Trim(), int.Parse(spaceParts[0].Trim()), int.Parse(spaceParts[1].Trim()));

            Stats  = new SearchStats(parts[3].Trim());
        }

        public string GetInterimReport()
        {
            string s = "";
            var settingsHeader = SearchSettings.GetHeaders();
            var statsHeader = SearchStats.GetHeaders();
            var settingsValues = Settings.GetValues();
            var statsValues = Stats.GetValues();

            PadAlign(settingsHeader, settingsValues);
            PadAlign(statsHeader, statsValues);

            s += "Interim Report:         " + string.Join(" | ", "Current board".PadLeft(CurrentBoardRepresentation.Length), string.Join(", ", settingsHeader), "start-end", string.Join(", ", statsHeader));
            s += "\nSearch continue string: " + string.Join(" | ", CurrentBoardRepresentation, string.Join(", ", settingsValues), $"{Settings.LastPieceStartPosition}-{Settings.LastPieceEndPosition}".PadLeft(9), string.Join(", ", statsValues));
            return s;
        }

        public string GetFinalReport()
        {
            var final = "FINAL REPORT\n";
            final += "------------";
            final += "\nPiece set: " + Settings.TypeSet;
            final += "\nTarget Score: " + Settings.TargetScore;
            final += "\nPiece placement rules: " + Settings.GetRuleString();
            final += "\nMajor boards evaluated: " + Stats.EvaluatedMajorBoardsCount;
            final += "\nAll boards evaluated: " + Stats.EvaluatedTotalBoardsCount;
            final += "\nSolutions Found: " + Stats.SolutionsCount;
            final += "\nTotal seconds: " + (Stats.TotalMilliSeconds / 1000);
            return final;
        }

        public static void PadAlign(List<string> a, List<string> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                var aLen = a[i].Length;
                var bLen = b[i].Length;
                if (aLen > bLen)
                {
                    b[i] = b[i].PadLeft(aLen);
                }
                else if (bLen > aLen)
                {
                    a[i] = a[i].PadLeft(bLen);
                }
            }
        }

        public override string ToString()
        {
            return string.Join(" | ", Settings , Stats );
        }
    }

    // something's a setting if changing it potentially impacts the result set 
    public class SearchSettings
    {
        public static List<string> GetHeaders() => new List<string> { "pieces", "dims", "target", "cover", "scoring", "rules" };
        public const int RankDim = 8;
        public const int FileDim = 8;

        public SearchSettings(string settingsData, int lastPieceStartPostion = 0, int lastPieceEndPosition = RankDim*FileDim)
        {
            var parts = settingsData.Split(',');

            var temp = parts[0].Trim().ToCharArray();
            Array.Sort(temp);
            TypeSet = new string(temp);  // need to ensure consistent ordering

            // right now dims modification unimplemented (parts[1])

            TargetScore = int.Parse(parts[2].Trim());

            // A for attack
            // Default is a piece covers its own square since that seems to be the more normal domination problem
            PieceCoversOwnSquare = parts[3].Trim() != "A";
            ScoreByPieceValue = parts[4].Trim() == "V";  // otherwise score by num pieces

            var optParams = parts[5].Trim();

            AllowRooksQueensOnSameRanksFiles = !optParams.Contains("R");
            AllowPawnsOnStartingRank = !optParams.Contains("P"); 
            var match = Regex.Match(optParams, "\\d+$");  // for consistency, anchored at end
            MaxNonRookNonPawnOnEdge = match.Success ? int.Parse(match.Value) : int.MaxValue;

            LastPieceStartPosition = lastPieceStartPostion;
            LastPieceEndPosition = lastPieceEndPosition;
        }

        // main settings
        public string TypeSet { get; }
        public int TargetScore { get; }
        public bool PieceCoversOwnSquare { get; }
        public bool ScoreByPieceValue { get; }

        // search segmentation for parallelism
        public int LastPieceStartPosition { get; }
        public int LastPieceEndPosition { get; }

        // defaults are set to be a comprehensive unfiltered search
        public bool AllowRooksQueensOnSameRanksFiles { get; }
        public bool AllowPawnsOnStartingRank { get; }
        // public bool MaintainBishopColour { get; }  todo - this would be a good option for looking for legal positions only
        public int MaxNonRookNonPawnOnEdge { get; }

        public List<string> GetValues()
        {
            return new List<string> 
            { 
                TypeSet, 
                GetDimString(), 
                TargetScore.ToString(), 
                PieceCoversOwnSquare ? "D" : "A",
                ScoreByPieceValue ? "V" : "N",
                GetRuleString()
            };
        }

        public string GetRuleString()
        {
            string ruleString = "";
            if (!AllowRooksQueensOnSameRanksFiles) ruleString += "R";
            if (!AllowPawnsOnStartingRank) ruleString += "P";
            if (MaxNonRookNonPawnOnEdge != int.MaxValue) ruleString += MaxNonRookNonPawnOnEdge;
            return ruleString;
        }

        public string GetDimString()
        {
            return string.Join("x", FileDim, RankDim);
        }
    }

    public class SearchStats
    {
        public static List<string> GetHeaders() => new List<string>{"majorevals", "totalevals", "sols", "elapsedS"};

        public SearchStats() { }
        public SearchStats(string commaSeparatedData)
        {
            var parts = commaSeparatedData.Split(',');
            EvaluatedMajorBoardsCount = long.Parse(parts[0].Trim());
            EvaluatedTotalBoardsCount = long.Parse(parts[1].Trim());
            SolutionsCount = long.Parse(parts[2].Trim());
            TotalMilliSeconds = long.Parse(parts[3].Trim()) * 1000;
        }

        public long EvaluatedMajorBoardsCount { get; set; }
        public long EvaluatedTotalBoardsCount { get; set; }
        public long SolutionsCount { get; set; } // long due to optimism
        public long TotalMilliSeconds { get; set; }

        public List<string> GetValues()
        {
            return new List<string> 
            {
                EvaluatedMajorBoardsCount.ToString(), 
                EvaluatedTotalBoardsCount.ToString(), 
                SolutionsCount.ToString(), 
                (TotalMilliSeconds / 1000).ToString()
            };
        }
    }
}