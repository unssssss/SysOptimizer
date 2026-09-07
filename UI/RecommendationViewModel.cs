using System.ComponentModel;
using System.Linq;
using SysOptimizer.Models;

namespace SysOptimizer.UI;

/// <summary>
/// wraps a Recommendation with an IsApproved bool the checkbox in the gui can bind to.
/// wpf needs INotifyPropertyChanged for two-way binding to actually update the ui
/// </summary>
public class RecommendationViewModel : INotifyPropertyChanged
{
    private bool _isApproved;

    public Recommendation Recommendation { get; }

    public RecommendationViewModel(Recommendation recommendation)
    {
        Recommendation = recommendation;
    }

    public bool IsApproved
    {
        get => _isApproved;
        set
        {
            if (_isApproved == value) return;
            _isApproved = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApproved)));
        }
    }

    // just flattening the fields out so the xaml can bind directly without a converter
    public string Id => Recommendation.Id;
    public string Title => Recommendation.Title;
    public string Category => Recommendation.Category;
    public string Description => Recommendation.Description;
    public string WhyItHelps => Recommendation.WhyItHelps;
    public string Evidence => Recommendation.Evidence;
    public string ExecutionScript => Recommendation.ExecutionScript;

    // little summary line so i don't need five separate textblocks for these flags
    public string Flags => string.Join("  ·  ", new[]
    {
        $"risk: {Recommendation.RiskLevel}",
        Recommendation.RequiresAdmin ? "needs admin" : null,
        Recommendation.RequiresReboot ? "needs reboot" : null,
        Recommendation.Reversible ? "reversible" : "not reversible"
    }.Where(s => s != null));

    public event PropertyChangedEventHandler? PropertyChanged;
}
