using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using unvell.ReoGrid;

namespace unvell.ReoGrid.Tests
{
	[TestFixture]
	public class WpfSheetTabMenuTest
	{
		[Test]
		[Apartment(ApartmentState.STA)]
		public void MenuInsertsBeforeTargetAndRenamesTarget()
		{
			var control = new ReoGridControl();
			try
			{
				control.NewWorksheet("Second");
				var menu = OpenMenu(control, 1);
				Assert.That(menu.Items.Count, Is.EqualTo(3));
				Assert.That(((MenuItem)menu.Items[0]).Header, Is.EqualTo(LanguageResource.Menu_InsertSheet));
				Assert.That(((MenuItem)menu.Items[1]).Header, Is.EqualTo(LanguageResource.Menu_DeleteSheet));
				Assert.That(((MenuItem)menu.Items[2]).Header, Is.EqualTo(LanguageResource.Menu_RenameSheet));
				((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
				Assert.That(control.Worksheets.Count, Is.EqualTo(3));
				Assert.That(control.Worksheets[1].Name, Is.EqualTo("Sheet2"));
				Assert.That(control.CurrentWorksheet, Is.SameAs(control.Worksheets[1]));
				control.Worksheets[1].Name = "Renamed";
				Assert.That(control.Worksheets[1].Name, Is.EqualTo("Renamed"));

				var renameItem = (MenuItem)OpenMenu(control, 1).Items[2];
				Assert.That(renameItem.Command, Is.Null);
				Assert.That(renameItem.Header, Is.EqualTo(LanguageResource.Menu_RenameSheet));
			}
			finally { control.Dispose(); }
		}

		[Test]
		[Apartment(ApartmentState.STA)]
		public void DeleteIsDisabledForLastWorksheetAndRemovesTargetOtherwise()
		{
			var control = new ReoGridControl();
			try
			{
				Assert.That(((MenuItem)OpenMenu(control, 0).Items[1]).IsEnabled, Is.False);
				control.NewWorksheet("Second");
				var deleteItem = (MenuItem)OpenMenu(control, 1).Items[1];
				Assert.That(deleteItem.IsEnabled, Is.True);
				deleteItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
				Assert.That(control.Worksheets.Count, Is.EqualTo(1));
				Assert.That(control.Worksheets[0].Name, Is.EqualTo("Sheet1"));
				Assert.That(control.CurrentWorksheet, Is.SameAs(control.Worksheets[0]));
			}
			finally { control.Dispose(); }
		}

		private static ContextMenu OpenMenu(ReoGridControl control, int index)
		{
			var showMenu = typeof(ReoGridControl).GetMethod("ShowSheetTabContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(showMenu, Is.Not.Null);
			showMenu.Invoke(control, new object[] { index });
			var menuField = typeof(ReoGridControl).GetField("sheetTabContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
			var menu = (ContextMenu)menuField.GetValue(control);
			menu.IsOpen = false;
			return menu;
		}
	}
}
