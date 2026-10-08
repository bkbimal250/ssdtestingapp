using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace pixinit.Views.Shared;

public partial class FirstPageView : UserControl
{
    public static readonly DependencyProperty DiagnosticCommandProperty = DependencyProperty.Register(nameof(DiagnosticCommand), typeof(ICommand), typeof(FirstPageView));
    public ICommand? DiagnosticCommand { get => (ICommand?)GetValue(DiagnosticCommandProperty); set => SetValue(DiagnosticCommandProperty, value); }
    public FirstPageView() => InitializeComponent();
}
