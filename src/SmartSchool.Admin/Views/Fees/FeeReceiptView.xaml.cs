using System.Windows.Controls;
using SmartSchool.Admin.ViewModels.Fees;

namespace SmartSchool.Admin.Views.Fees;

public partial class FeeReceiptView : UserControl
{
    /// <summary>The receipt FlowDocument is built by the VM and injected here by the host.</summary>
    public FeeReceiptView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FeeReceiptViewModel vm)
                ReceiptHost.Document = vm.BuildReceiptDocument();
        };
    }
}
