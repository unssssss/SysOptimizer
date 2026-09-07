using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using SysOptimizer.Models;

namespace SysOptimizer.UI;

public partial class ApprovalWindow : Window
{
    public ObservableCollection<RecommendationViewModel> Recommendations { get; }
    public List<string> ApprovedIds { get; private set; } = new();

    public ApprovalWindow(IEnumerable<Recommendation> recommendations)
    {
        InitializeComponent();

        Recommendations = new ObservableCollection<RecommendationViewModel>(
            recommendations.Select(r => new RecommendationViewModel(r)));

        // wiring this up directly instead of a DataContext binding, just simpler to
        // follow for anyone (me, later) still getting used to how wpf binding works
        RecommendationsList.ItemsSource = Recommendations;
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        ApprovedIds = Recommendations.Where(r => r.IsApproved).Select(r => r.Id).ToList();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ApprovedIds = new List<string>();
        DialogResult = false;
        Close();
    }
}
