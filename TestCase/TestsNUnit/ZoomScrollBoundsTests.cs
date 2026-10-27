using NUnit.Framework;

namespace unvell.ReoGrid.Tests.TestsNUnit
{
	[TestFixture]
	public class ZoomScrollBoundsTests
	{
		[Test]
		public void ZoomingKeepsLastContentReachable()
		{
			using (var control = new ReoGridControl())
			{
				control.Size = new System.Drawing.Size(640, 480);
				using (var form = new System.Windows.Forms.Form())
				{
					form.Controls.Add(control);
					form.Show();
					System.Windows.Forms.Application.DoEvents();
					var sheet = control.Worksheets[0];
					sheet.Resize(100, 100);

					for (int i = 0; i < 8; i++)
					{
						sheet.SetScale(0.4f + i * 0.2f);
						var horizontalBar = (System.Windows.Forms.HScrollBar)control.Controls[0];
						var verticalBar = (System.Windows.Forms.VScrollBar)control.Controls[1];
						horizontalBar.Value = horizontalBar.Maximum;
						verticalBar.Value = verticalBar.Maximum;
						System.Windows.Forms.Application.DoEvents();

						var lastCell = sheet.GetRangePhysicsBounds(new RangePosition(99, 99, 1, 1));
						Assert.That(lastCell.Right,
							Is.LessThanOrEqualTo(control.ClientSize.Width + 1), "last column should be reachable at scale " + sheet.ScaleFactor);
						Assert.That(lastCell.Bottom,
							Is.LessThanOrEqualTo(control.ClientSize.Height + 1), "last row should be reachable at scale " + sheet.ScaleFactor);
					}
					form.Close();
				}
			}
		}
	}
}
