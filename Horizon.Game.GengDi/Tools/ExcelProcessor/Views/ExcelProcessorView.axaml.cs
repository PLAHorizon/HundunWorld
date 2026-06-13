using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Horizon.Game.GengDi.Tools.ExcelProcessor.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Tools.ExcelProcessor.Views
{
    public partial class ExcelProcessorView : UserControl
    {
        private ExcelProcessorViewModel _vm = null!;

        public ExcelProcessorView()
        {
            InitializeComponent();
            _vm = new ExcelProcessorViewModel
            {
                PickSourceFilesAsync = DoPickSourceFilesAsync,
                PickTargetFileAsync = DoPickTargetFileAsync
            };
            DataContext = _vm;
        }

        private async Task<IEnumerable<string>?> DoPickSourceFilesAsync()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Excel 文件",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Excel 文件") { Patterns = new[] { "*.xlsx", "*.xls", "*.csv" } }
                }
            });

            return files.Select(f => f.Path.LocalPath);
        }

        private async Task<string?> DoPickTargetFileAsync()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存目标文件",
                SuggestedFileName = $"合并结果_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Excel 文件 (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
                    new FilePickerFileType("CSV 文件 (*.csv)") { Patterns = new[] { "*.csv" } }
                }
            });

            return file?.Path.LocalPath;
        }
    }
}
