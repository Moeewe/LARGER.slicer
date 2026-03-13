using System;

namespace LARGERslicer.Types
{
    public class ThekenBoard
    {
        public double ZMin { get; set; }
        public double ZMax { get; set; }
        public double Thickness => ZMax - ZMin;
        public string Type { get; set; } = "Mittelbrett";
        public bool IsSplitPart { get; set; }
        public int SourceIndex { get; set; }
        public bool ContainsFuge { get; set; }
        public double FugeCenter { get; set; }
        public double FugeWidth { get; set; }

        public override string ToString()
        {
            string fugeInfo = ContainsFuge ? $" | Fuge={FugeCenter:F2} +/- {FugeWidth * 0.5:F2}" : "";
            return $"{Type}: Z {ZMin:F2}-{ZMax:F2} ({Thickness:F2} mm){fugeInfo}";
        }
    }

    public class ThekenBoardWithDepth
    {
        public ThekenBoard Board { get; set; }
        public double Depth { get; set; }
        public double Length { get; set; }

        public override string ToString()
        {
            if (Board == null)
                return "Invalid board";
            return $"{Board.Type}: L={Length:F1} D={Depth:F1} H={Board.Thickness:F1}";
        }
    }
}
