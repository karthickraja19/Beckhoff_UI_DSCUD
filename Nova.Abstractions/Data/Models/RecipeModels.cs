using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Data.Models
{
    public sealed class GantryPositionRecord
    {
        public string Position { get; set; } = string.Empty;
        public string X { get; set; } = "0";
        public string XVel { get; set; } = "0";
        public string XAcc { get; set; } = "0";
        public string XDeAcc { get; set; } = "0";
        public string Y { get; set; } = "0";
        public string YVel { get; set; } = "0";
        public string YAcc { get; set; } = "0";
        public string YDeAcc { get; set; } = "0";
        public string Z { get; set; } = "0";
        public string ZVel { get; set; } = "0";
        public string ZAcc { get; set; } = "0";
        public string ZDeAcc { get; set; } = "0";
    }

    public sealed class RobotPointRecord
    {
        public string PointName { get; set; } = string.Empty;
        public string Value { get; set; } = "0";
    }

    public sealed class LightChannelRecord
    {
        public string ColourCode { get; set; } = string.Empty;
        public string Colour { get; set; } = string.Empty;
        public string Value { get; set; } = "0";
    }

    public sealed class GantryModuleDefinition
    {
        public string ModuleName { get; set; } = string.Empty;
        public IReadOnlyList<string> MovementPriority { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> SafePositionPriority { get; set; } = Array.Empty<string>();
    }

    public enum RecipeSaveStatus
    {
        Saved = 0,
        NoChanges = 1,
    }

    public sealed class RecipeSaveResult
    {
        public RecipeSaveStatus Status { get; }
        public string Message { get; }

        public bool IsSaved => Status == RecipeSaveStatus.Saved;
        public bool IsNoChanges => Status == RecipeSaveStatus.NoChanges;

        private RecipeSaveResult(RecipeSaveStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public static RecipeSaveResult Saved(string message)
        {
            return new RecipeSaveResult(RecipeSaveStatus.Saved, message);
        }

        public static RecipeSaveResult NoChanges(string message)
        {
            return new RecipeSaveResult(RecipeSaveStatus.NoChanges, message);
        }
    }
}
