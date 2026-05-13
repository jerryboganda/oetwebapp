using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

public static class ListeningPathwayLaunchTargets
{
    public static string? BuildActionHref(string stage, ListeningPathwayStageStatus status, string? paperId)
    {
        if (status is not (ListeningPathwayStageStatus.Unlocked or ListeningPathwayStageStatus.InProgress))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(paperId))
        {
            return null;
        }

        var mode = ModeForStage(stage);
        if (mode is null)
        {
            return null;
        }

        return $"/listening/player/{Uri.EscapeDataString(paperId)}?mode={Uri.EscapeDataString(mode)}&pathwayStage={Uri.EscapeDataString(stage)}";
    }

    public static string? ModeForStage(string stage) => stage switch
    {
        "diagnostic" => "diagnostic",
        "foundation_partA" or "foundation_partB" or "foundation_partC"
            or "drill_partA" or "drill_partB" or "drill_partC"
            or "minitest_partA" or "minitest_partBC" => "practice",
        "fullpaper_paper" => "paper",
        "fullpaper_cbt" => "exam",
        "exam_simulation" => "home",
        _ => null,
    };
}