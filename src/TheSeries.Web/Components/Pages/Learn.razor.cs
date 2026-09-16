using Microsoft.AspNetCore.Components;
using TheSeries.Web.Learning;

namespace TheSeries.Web.Components.Pages;

/// <summary>A standalone, URL-addressable guide using the existing concept catalog without an AI service dependency.</summary>
public partial class Learn
{
    [Inject]
    private ConceptCatalog Concepts { get; set; } = default!;

    /// <summary>The optional stable stage identifier supplied by the query string.</summary>
    [SupplyParameterFromQuery(Name = "stage")]
    public string? StageId { get; set; }

    private static IReadOnlyList<LearningStage> Stages => AgentLearningJourney.Stages;
    private LearningStage _stage = Stages[0];
    private int _stageIndex;
    private bool _initialized;
    private Concept? _activeConcept;
    private ElementReference _stageHeading;
    private ElementReference _conceptHeading;
    private ElementReference _relatedHeading;
    private FocusTarget _pendingFocus;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var next = AgentLearningJourney.Resolve(StageId);
        if (_initialized && next.Id != _stage.Id)
        {
            _pendingFocus = FocusTarget.Stage;
        }

        _activeConcept = null;
        _stage = next;
        _stageIndex = AgentLearningJourney.IndexOf(next.Id);
        _initialized = true;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var target = _pendingFocus;
        _pendingFocus = FocusTarget.None;
        if (target != FocusTarget.None)
        {
            var element = target switch
            {
                FocusTarget.Concept => _conceptHeading,
                FocusTarget.Related => _relatedHeading,
                _ => _stageHeading,
            };
            await element.FocusAsync();
        }
    }

    private void OpenConcept(Concept concept)
    {
        _activeConcept = concept;
        _pendingFocus = FocusTarget.Concept;
    }

    private void CloseConcept()
    {
        _activeConcept = null;
        _pendingFocus = FocusTarget.Related;
    }

    private enum FocusTarget { None, Stage, Concept, Related }
}