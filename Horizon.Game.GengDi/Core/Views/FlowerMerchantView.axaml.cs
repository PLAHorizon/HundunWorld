using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Linq;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerMerchantView : UserControl
    {
        public FlowerMerchantView()
        {
            DiagLog.Log($"[FlowerMerchantView] ctor START");
            try
            {
                DiagLog.Log("[FlowerMerchantView] before InitializeComponent");
                InitializeComponent();
                DiagLog.Log("[FlowerMerchantView] after InitializeComponent");
            }
            catch (Exception ex)
            {
                DiagLog.Log($"[FlowerMerchantView] InitializeComponent THREW: {ex}");
                throw;
            }

            // 关键修复：DataContext 赋值推迟到 Loaded 事件。
            // 诊断日志显示第二次导航时 UI 线程卡在构造函数中的 DataContext 赋值（触发
            // Avalonia 绑定初始化），此时 View 尚未加入可视化树。将 DataContext 赋值
            // 推迟到 Loaded 事件（View 已在可视化树中），绑定初始化在正确的时机执行。
            Loaded += OnFlowerMerchantViewLoaded;
            DiagLog.Log("[FlowerMerchantView] ctor END");
        }

        private void OnFlowerMerchantViewLoaded(object sender, RoutedEventArgs e)
        {
            DiagLog.Log("[FlowerMerchantView] Loaded START");
            Loaded -= OnFlowerMerchantViewLoaded;
            try
            {
                DiagLog.Log("[FlowerMerchantView] before new FlowerMerchantViewModel");
                var vm = new FlowerMerchantViewModel();
                DiagLog.Log("[FlowerMerchantView] VM created, before DataContext set");
                DataContext = vm;
                DiagLog.Log("[FlowerMerchantView] after DataContext set");

                var publishSpeciesComboBox = this.FindControl<ComboBox>("PublishSpeciesComboBox");
                if (publishSpeciesComboBox != null)
                {
                    publishSpeciesComboBox.SelectionChanged += OnPublishSpeciesSelectionChanged;
                }

                vm.StartInitialization();
            }
            catch (Exception ex)
            {
                DiagLog.Log($"[FlowerMerchantView] Loaded THREW: {ex}");
            }
            DiagLog.Log("[FlowerMerchantView] Loaded END");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnPublishProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.CreateProductAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnRegisterMerchantClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.RegisterMerchantAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnActivateProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                        await vm.ActivateProductAsync(product);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void OnEditProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                vm.OpenEditProductDialog(product);
        }

        private async void OnSaveEditProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.SaveEditProductAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void OnCancelEditProductClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.ShowEditProductDialog = false;
        }

        private void OnOpenFreightTemplateDialogClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.OpenFreightTemplateDialog();
        }

        private async void OnSaveFreightTemplateClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.SaveFreightTemplateAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void OnCancelFreightTemplateClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.ShowFreightTemplateDialog = false;
        }

        private async void OnDeleteFreightTemplateClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long templateId)
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    await vm.DeleteFreightTemplateAsync(templateId);
            }
        }

        private async void OnAddSkuClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.AddEditProductSKUAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnDeleteSkuClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long skuId)
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    await vm.DeleteEditProductSKUAsync(skuId);
            }
        }

        private async void OnDeactivateProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                        await vm.DeactivateProductAsync(product);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnDeleteProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                        await vm.DeleteProductAsync(product);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void OnTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var tab))
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    vm.CurrentTab = tab;

                UpdateFilterButtonStyle(btn);
            }
        }

        private void OnRegisterTypeClick(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out var type))
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    vm.RegisterType = type;
            }
        }

        private void OnCategoryFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var speciesId))
            {
                if (DataContext is FlowerMerchantViewModel vm)
                {
                    if (speciesId == 0)
                        vm.SelectedCategory = null;
                    else
                        vm.SelectedCategory = vm.Categories.FirstOrDefault(c => c.Id == speciesId);
                    vm.SearchProductsCommand.Execute(null);
                }

                UpdateFilterButtonStyle(btn);
            }
        }

        private void OnOrderStatusFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var status))
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    vm.OrderStatusFilter = status;

                UpdateFilterButtonStyle(btn);
            }
        }

        private void OnRefundStatusFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var status))
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    vm.RefundStatusFilter = status;

                UpdateFilterButtonStyle(btn);
            }
        }

        private static void UpdateFilterButtonStyle(Button selectedBtn)
        {
            if (selectedBtn.Parent is StackPanel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Button childBtn)
                    {
                        childBtn.Classes.Remove("PrimaryAction");
                        if (!childBtn.Classes.Contains("QuietAction"))
                            childBtn.Classes.Add("QuietAction");
                    }
                }
                selectedBtn.Classes.Remove("QuietAction");
                if (!selectedBtn.Classes.Contains("PrimaryAction"))
                    selectedBtn.Classes.Add("PrimaryAction");
            }
        }

        private async void OnLocalImageUploadClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var storageProvider = topLevel.StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择商品图片",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp" }
                    }
                }
            });

            if (result == null || result.Count == 0) return;

            if (DataContext is FlowerMerchantViewModel vm)
            {
                var localImageDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HundunWorld", "product_images");

                if (!Directory.Exists(localImageDir))
                    Directory.CreateDirectory(localImageDir);

                foreach (var file in result)
                {
                    try
                    {
                        var sourcePath = file.TryGetLocalPath();
                        if (string.IsNullOrEmpty(sourcePath)) continue;

                        var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Path.GetFileName(sourcePath)}";
                        var destPath = Path.Combine(localImageDir, fileName);
                        File.Copy(sourcePath, destPath, true);

                        vm.AddImage(destPath);
                    }
                    catch { }
                }
            }
        }

        private async void OnOnlineImageParseClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var dialog = new Window
            {
                Title = "在线图片解析",
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Topmost = true
            };

            var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "请输入在线图片URL", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold });

            var urlBox = new TextBox
            {
                Watermark = "https://example.com/image.jpg",
                Padding = new Thickness(12, 8),
                CornerRadius = new Avalonia.CornerRadius(8)
            };
            panel.Children.Add(urlBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var confirmBtn = new Button
            {
                Content = "确认解析",
                Classes = { "PrimaryAction" },
                Padding = new Thickness(16, 8),
                CornerRadius = new Avalonia.CornerRadius(8)
            };
            confirmBtn.Click += async (s, ev) =>
            {
                var url = urlBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(url)) return;

                dialog.Close();

                if (DataContext is FlowerMerchantViewModel vm)
                {
                    try
                    {
                        using var httpClient = new System.Net.Http.HttpClient();
                        httpClient.Timeout = TimeSpan.FromSeconds(15);
                        var response = await httpClient.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                            if (contentType.StartsWith("image/"))
                            {
                                var localImageDir = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "HundunWorld", "product_images");

                                if (!Directory.Exists(localImageDir))
                                    Directory.CreateDirectory(localImageDir);

                                var ext = contentType.Split('/').LastOrDefault() ?? "jpg";
                                if (ext == "jpeg") ext = "jpg";
                                var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{ext}";
                                var destPath = Path.Combine(localImageDir, fileName);

                                var bytes = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(destPath, bytes);

                                vm.AddImage(destPath);
                            }
                            else
                            {
                                Horizon.Game.GengDi.Core.Controls.ToastService.Instance.Error("URL返回的不是图片");
                            }
                        }
                        else
                        {
                            Horizon.Game.GengDi.Core.Controls.ToastService.Instance.Error($"下载图片失败: HTTP {(int)response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Horizon.Game.GengDi.Core.Controls.ToastService.Instance.Error($"解析失败: {ex.Message}");
                    }
                }
            };
            btnPanel.Children.Add(confirmBtn);

            var cancelBtn = new Button
            {
                Content = "取消",
                Classes = { "QuietAction" },
                Padding = new Thickness(16, 8),
                CornerRadius = new Avalonia.CornerRadius(8)
            };
            cancelBtn.Click += (s, ev) => dialog.Close();
            btnPanel.Children.Add(cancelBtn);

            panel.Children.Add(btnPanel);
            dialog.Content = panel;
            await dialog.ShowDialog(topLevel as Window);
        }

        private void OnRemoveImageClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string imagePath)
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    vm.RemoveImage(imagePath);
            }
        }

        private void OnMoveUpProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                vm.MoveProductUp(product);
        }

        private void OnMoveDownProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                vm.MoveProductDown(product);
        }

        private void OnAuditProductClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RelatedProduct product && DataContext is FlowerMerchantViewModel vm)
                vm.OpenAuditDialog(product);
        }

        private void OnAuditOverlayTapped(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.ShowAuditDialog = false;
        }

        private void OnAuditRadioClick(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out var approved))
            {
                if (DataContext is FlowerMerchantViewModel vm)
                    vm.AuditApproved = approved;
            }
        }

        private void OnCancelAuditClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.ShowAuditDialog = false;
        }

        private async void OnConfirmAuditClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.ConfirmAuditAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void OnEditDrawerOverlayTapped(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.ShowEditProductDialog = false;
        }

        private void OnPublishSpeciesSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is SpeciesFilterItem item && DataContext is FlowerMerchantViewModel vm)
            {
                vm.SelectedSpeciesId = item.SpeciesId;
            }
        }

        private void OnShowPriceAdjustmentsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                _ = vm.LoadPriceAdjustmentSuggestionsAsync();
        }

        private void OnAdjustPriceClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PriceAdjustmentSuggestionInfo suggestion && DataContext is FlowerMerchantViewModel vm)
                vm.OpenPriceAdjustDialog(suggestion);
        }

        private void OnCancelPriceAdjustClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is FlowerMerchantViewModel vm)
                vm.ShowPriceAdjustDialog = false;
        }

        private async void OnConfirmPriceAdjustClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                try
                {
                    if (DataContext is FlowerMerchantViewModel vm)
                        await vm.ConfirmPriceAdjustAsync();
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }
    }
}
