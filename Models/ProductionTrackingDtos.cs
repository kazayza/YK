namespace YKCoatings.Models
{
    // ==========================================
    // ملخص أمر تصنيع حيّ في الداشبورد
    // ==========================================
    public class CurrentProductionOrderDto
    {
        public int ProductionOrderID { get; set; }
        public string OrderNumber { get; set; } = "";
        public string ProductName { get; set; } = "";

        public int OrderStatus { get; set; }
        public string OrderStatusName { get; set; } = "";

        public int ActiveBatchCount { get; set; }
        public int? CurrentBatchID { get; set; }
        public string CurrentBatchNumber { get; set; } = "";

        public string CurrentStageName { get; set; } = "";
        public int CurrentStageStatus { get; set; }
        public string CurrentStageStatusName { get; set; } = "";

        public DateTime? StageStartTime { get; set; }
        public int ElapsedMinutes { get; set; }

        public string OperatorName { get; set; } = "";
        public string Temperature { get; set; } = "";
        public string Speed { get; set; } = "";

        public int CompletedStages { get; set; }
        public int TotalStages { get; set; }

        public decimal ProgressPercent =>
            TotalStages > 0 ? Math.Round((decimal)CompletedStages * 100m / TotalStages, 1) : 0;

        public string ElapsedText
        {
            get
            {
                if (!StageStartTime.HasValue || ElapsedMinutes <= 0)
                    return "—";

                if (ElapsedMinutes < 60)
                    return $"{ElapsedMinutes} دقيقة";

                var hours = ElapsedMinutes / 60;
                var mins = ElapsedMinutes % 60;

                if (mins == 0)
                    return $"{hours} ساعة";

                return $"{hours} س {mins} د";
            }
        }
    }

    // ==========================================
    // تفاصيل الباتشات الحية لأمر تصنيع واحد
    // ==========================================
    public class CurrentProductionBatchDto
    {
        public int BatchID { get; set; }
        public string BatchNumber { get; set; } = "";

        public int BatchStatus { get; set; }
        public string BatchStatusName { get; set; } = "";

        public string CurrentStageName { get; set; } = "";
        public int CurrentStageStatus { get; set; }
        public string CurrentStageStatusName { get; set; } = "";

        public DateTime? StageStartTime { get; set; }
        public int ElapsedMinutes { get; set; }

        public string OperatorName { get; set; } = "";
        public string Temperature { get; set; } = "";
        public string Speed { get; set; } = "";

        public int CompletedStages { get; set; }
        public int TotalStages { get; set; }

        public decimal ProgressPercent =>
            TotalStages > 0 ? Math.Round((decimal)CompletedStages * 100m / TotalStages, 1) : 0;

        public string ElapsedText
        {
            get
            {
                if (!StageStartTime.HasValue || ElapsedMinutes <= 0)
                    return "—";

                if (ElapsedMinutes < 60)
                    return $"{ElapsedMinutes} دقيقة";

                var hours = ElapsedMinutes / 60;
                var mins = ElapsedMinutes % 60;

                if (mins == 0)
                    return $"{hours} ساعة";

                return $"{hours} س {mins} د";
            }
        }
    }

    // ==========================================
    // عنصر مرحلة في الخط الزمني
    // ==========================================
    public class StageTimelineItemDto
    {
        public int ProductionOrderID { get; set; }
        public int BatchID { get; set; }
        public int StageOrder { get; set; }
        public string StageName { get; set; } = "";
        public int StageStatus { get; set; }
        public string StageStatusName { get; set; } = "";
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int? DurationMinutes { get; set; }
        public string OperatorName { get; set; } = "";

        public string DurationText
        {
            get
            {
                if (!DurationMinutes.HasValue || DurationMinutes.Value <= 0)
                    return "";

                if (DurationMinutes.Value < 60)
                    return $"{DurationMinutes.Value} د";

                var h = DurationMinutes.Value / 60;
                var m = DurationMinutes.Value % 60;

                if (m == 0)
                    return $"{h} س";

                return $"{h} س {m} د";
            }
        }
    }
}