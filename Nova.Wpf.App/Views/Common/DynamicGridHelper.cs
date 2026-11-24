using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Nova.Abstractions.Data.Models;
using Nova.Wpf.UI.Behaviors;
using Nova.Wpf.UI.Localization;

namespace Nova.Wpf.App.Views.Common
{
    /// <summary>
    /// Attached behavior that generates WPF DataGrid columns dynamically from a collection of DynamicColumnSchema.
    /// Implements universal column alignment:
    /// - Col 0 (Identifier / Key): Left-aligned with comfortable padding & semi-bold text.
    /// - Cols 1..N (Values / Numeric Parameters): Center-aligned with center-aligned headers.
    /// </summary>
    public static class DynamicGridHelper
    {
        public static readonly DependencyProperty ColumnsSourceProperty =
            DependencyProperty.RegisterAttached(
                "ColumnsSource",
                typeof(IEnumerable<DynamicColumnSchema>),
                typeof(DynamicGridHelper),
                new PropertyMetadata(null, OnColumnsSourceChanged));

        public static IEnumerable<DynamicColumnSchema> GetColumnsSource(DependencyObject obj)
        {
            return (IEnumerable<DynamicColumnSchema>)obj.GetValue(ColumnsSourceProperty);
        }

        public static void SetColumnsSource(DependencyObject obj, IEnumerable<DynamicColumnSchema> value)
        {
            obj.SetValue(ColumnsSourceProperty, value);
        }

        private static void OnColumnsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid dataGrid)
            {
                if (e.OldValue is INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= (s, args) => GenerateColumns(dataGrid, GetColumnsSource(dataGrid));
                }

                if (e.NewValue is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += (s, args) => GenerateColumns(dataGrid, GetColumnsSource(dataGrid));
                }

                GenerateColumns(dataGrid, e.NewValue as IEnumerable<DynamicColumnSchema>);
            }
        }

        /// <summary>What a value cell may be typed with, from its column's type.</summary>
        private static NumericInput.NumericMode InputModeFor(DynamicColumnSchema col)
        {
            if (col.IsIpAddress)
                return NumericInput.NumericMode.IpAddress;

            switch (col.DataType)
            {
                case ColumnDataType.Numeric: return NumericInput.NumericMode.SignedDecimal;
                case ColumnDataType.NonNegative: return NumericInput.NumericMode.Decimal;
                case ColumnDataType.Integer: return NumericInput.NumericMode.Integer;
                default: return NumericInput.NumericMode.None;
            }
        }

        public static void GenerateColumns(DataGrid dataGrid, IEnumerable<DynamicColumnSchema> columns)
        {
            if (dataGrid == null) return;
            dataGrid.Columns.Clear();
            if (columns == null) return;

            int index = 0;
            foreach (var col in columns)
            {
                bool isIdentifier = col.IsIdentifier;

                if (isIdentifier)
                {
                    // Column 0: Left-Aligned Row Identifier
                    var headerStyle = new Style(typeof(DataGridColumnHeader), (Style)dataGrid.FindResource("Nova.DataGridColumnHeader"));
                    headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
                    headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 8, 0)));

                    var elementStyle = new Style(typeof(TextBlock));
                    elementStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(12, 0, 8, 0)));
                    elementStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
                    elementStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                    elementStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));

                    var textCol = new DataGridTextColumn
                    {
                        Header = col.Title,
                        HeaderTemplate = WrappingHeader(TextAlignment.Left),
                        // Row names (CCD1, Robot, axis names) in the selected language; read-only column.
                        Binding = LocalizationSource.TranslatedBinding("[" + col.Key + "]"),
                        IsReadOnly = true,
                        ElementStyle = elementStyle,
                        HeaderStyle = headerStyle
                    };

                    if (col.Width.HasValue)
                    {
                        textCol.Width = new DataGridLength(col.Width.Value);
                    }
                    else
                    {
                        // A wide table (the gantry's 13 columns) would otherwise squeeze
                        // the row names down to an ellipsis.
                        textCol.Width = new DataGridLength(1.4, DataGridLengthUnitType.Star);
                        textCol.MinWidth = IdentifierMinWidth;
                    }
                    dataGrid.Columns.Add(textCol);
                }
                else
                {
                    // Columns 1..N: Center-Aligned Value Cells with Centered Headers
                    var headerStyle = new Style(typeof(DataGridColumnHeader), (Style)dataGrid.FindResource("Nova.DataGridColumnHeader"));
                    headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
                    headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 0, 4, 0)));

                    var templateCol = new DataGridTemplateColumn
                    {
                        Header = col.Title,
                        HeaderTemplate = WrappingHeader(TextAlignment.Center),
                        HeaderStyle = headerStyle,
                        Width = col.Width.HasValue ? new DataGridLength(col.Width.Value) : new DataGridLength(1, DataGridLengthUnitType.Star)
                    };

                    var cellTemplate = new DataTemplate();
                    var factory = new FrameworkElementFactory(typeof(TextBox));
                    factory.SetResourceReference(FrameworkElement.StyleProperty, "ValueCell");
                    factory.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Center);
                    factory.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);

                    // FAM 2 SET_02 / SET_05: a number or IP address cell refuses letters and
                    // other characters at the keyboard; the row still validates what it holds.
                    NumericInput.NumericMode inputMode = InputModeFor(col);
                    if (inputMode != NumericInput.NumericMode.None)
                        factory.SetValue(NumericInput.ModeProperty, inputMode);

                    // Dynamic grids strictly launch locked in read-only mode until Edit mode is toggled
                    factory.SetBinding(TextBox.IsReadOnlyProperty, new Binding("DataContext.IsReadOnlyMode")
                    {
                        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
                        FallbackValue = true
                    });

                    factory.SetBinding(TextBox.TextProperty, new Binding("[" + col.Key + "]")
                    {
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                        ValidatesOnNotifyDataErrors = true,
                        Mode = BindingMode.TwoWay
                    });
                    cellTemplate.VisualTree = factory;
                    templateCol.CellTemplate = cellTemplate;

                    dataGrid.Columns.Add(templateCol);
                }
                index++;
            }
        }

        private const double IdentifierMinWidth = 120;

        /// <summary>
        /// Column title that wraps onto a second line when the column is narrower than
        /// the title, instead of being clipped mid-word ("X-Vel (mn").
        /// </summary>
        private static DataTemplate WrappingHeader(TextAlignment alignment)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            // Column titles come from the settings files in English; shown in the selected language.
            text.SetBinding(TextBlock.TextProperty, LocalizationSource.TranslatedBinding(null));
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.TextAlignmentProperty, alignment);
            text.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4));
            return new DataTemplate { VisualTree = text };
        }
    }
}
