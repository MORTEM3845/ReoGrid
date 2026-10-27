using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using unvell.ReoGrid;
using unvell.ReoGrid.Graphics;
using unvell.ReoGrid.Main;
using unvell.ReoGrid.WPF;

namespace unvell.ReoGrid.Tests.WPF
{
	[TestFixture]
	[Apartment(System.Threading.ApartmentState.STA)]
	public class SheetTabAppearanceTests
	{
		[Test]
		public void ControlStyleSheetTabColorsAreAppliedToExistingAndNewTabs()
		{
			var control = new ReoGridControl();
			control.Worksheets.Add(control.CreateWorksheet("First"));
			control.Worksheets.Add(control.CreateWorksheet("Second"));

			var style = ControlAppearanceStyle.CreateDefaultControlStyle();
			var border = new SolidColor(255, 220, 20, 60);
			var selected = new SolidColor(255, 30, 144, 255);
			var text = new SolidColor(255, 255, 215, 0);
			style[ControlAppearanceColors.SheetTabBorder] = border;
			style[ControlAppearanceColors.SheetTabSelected] = selected;
			style[ControlAppearanceColors.SheetTabText] = text;

			// Reproduce the public API sequence from issue #547.
			control.ControlStyle = style;

			var sheetTab = (DependencyObject)typeof(ReoGridControl)
				.GetField("sheetTab", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(control);
			var sheetTabType = sheetTab.GetType();
			Assert.That(sheetTabType.GetProperty("BorderColor").GetValue(sheetTab), Is.EqualTo((Color)border));
			Assert.That(sheetTabType.GetProperty("SelectedBackColor").GetValue(sheetTab), Is.EqualTo((Color)selected));
			Assert.That(sheetTabType.GetProperty("SelectedTextColor").GetValue(sheetTab), Is.EqualTo((Color)text));

			var existingLabels = FindTextBlocks(sheetTab).ToArray();
			Assert.That(existingLabels, Is.Not.Empty);
			Assert.That(existingLabels.Any(label => label.Foreground is SolidColorBrush brush && brush.Color == (Color)text), Is.True);

			control.Worksheets.Add(control.CreateWorksheet("Third"));
			var allLabels = FindTextBlocks(sheetTab).ToArray();
			Assert.That(allLabels.Length, Is.EqualTo(existingLabels.Length + 1));
			Assert.That(allLabels.Any(label => label.Foreground is SolidColorBrush brush && brush.Color == (Color)text), Is.True);
		}

		private static System.Collections.Generic.IEnumerable<TextBlock> FindTextBlocks(DependencyObject root)
		{
			for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
			{
				var child = VisualTreeHelper.GetChild(root, index);
				if (child is TextBlock textBlock)
				{
					yield return textBlock;
				}

				foreach (var descendant in FindTextBlocks(child))
				{
					yield return descendant;
				}
			}
		}
	}
}
