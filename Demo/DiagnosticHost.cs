using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using unvell.ReoGrid;

namespace unvell.ReoGrid.Demo
{
	internal static class DiagnosticHost
	{
		public static bool TryStart(string[] args)
		{
			if (args.Length != 3 || args[0] != "--diagnostic")
			{
				return false;
			}

			string profile = Path.GetFullPath(args[1]);
			try
			{
				Application.Run(new DiagnosticForm(profile, args[2]));
			}
			catch (Exception ex)
			{
				Directory.CreateDirectory(profile);
				File.AppendAllText(Path.Combine(profile, "diagnostic-error.log"), DateTimeOffset.UtcNow + " " + ex + Environment.NewLine);
			}
			return true;
		}
	}

	internal sealed class DiagnosticForm : Form
	{
		private readonly string sessionDirectory;
		private readonly string desktopName;
		private readonly string pipeName;
		private readonly ReoGridControl grid;
		private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
		private Task serverTask;

		public DiagnosticForm(string sessionDirectory, string desktopName)
		{
			this.sessionDirectory = sessionDirectory;
			this.desktopName = desktopName;
			Directory.CreateDirectory(sessionDirectory);
			pipeName = "reogrid-diagnostic-" + Guid.NewGuid().ToString("N");
			Text = "ReoGrid Diagnostic";
			Width = 1100;
			Height = 760;
			ShowInTaskbar = false;
			grid = new ReoGridControl { Dock = DockStyle.Fill };
			Controls.Add(grid);
			InitializeWorkbook();
		}

		protected override CreateParams CreateParams
		{
			get
			{
				var parameters = base.CreateParams;
				parameters.ExStyle |= 0x08000000;
				return parameters;
			}
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			File.WriteAllText(Path.Combine(sessionDirectory, "session.json"), JsonSerializer.Serialize(new
			{
				protocol = 1,
				pipe = pipeName,
				pid = Environment.ProcessId,
				executable = Application.ExecutablePath,
				startedUtc = DateTimeOffset.UtcNow,
				profile = sessionDirectory,
				desktop = desktopName,
				build = typeof(DiagnosticForm).Assembly.ManifestModule.ModuleVersionId
			}));
			serverTask = Task.Run(() => ServeAsync(shutdown.Token));
		}

		private void InitializeWorkbook()
		{
			var sheet = grid.CurrentWorksheet;
			sheet.Name = "Diagnostic";
			sheet.SetRangeData("A1:D6", new object[,]
			{
				{ "Product", "Units", "Price", "Total" },
				{ "Notebook", 12, 4.5, 54 },
				{ "Pen", 30, 1.25, 37.5 },
				{ "Folder", 8, 2.75, 22 },
				{ "Marker", 15, 3.2, 48 },
				{ "Grand total", null, null, 161.5 },
			});
			sheet.SetRangeDataFormat("A1:D1", unvell.ReoGrid.DataFormat.CellDataFormatFlag.Text);
			sheet.SetColumnsWidth(0, 4, 130);
			sheet.SelectionRange = new RangePosition("A1:D1");
		}

		private async Task ServeAsync(CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
					PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
				{
					try
					{
						await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
						using (var reader = new StreamReader(pipe, leaveOpen: true))
						using (var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true })
						{
							string line = await reader.ReadLineAsync().ConfigureAwait(false);
							try
							{
								using (var request = JsonDocument.Parse(line))
								{
									JsonElement root = request.RootElement;
									string command = root.GetProperty("command").GetString();
									object result = await OnUiAsync(() => Execute(command, root)).ConfigureAwait(false);
									await writer.WriteLineAsync(JsonSerializer.Serialize(new { status = "completed", result })).ConfigureAwait(false);
								}
							}
							catch (Exception ex)
							{
								await writer.WriteLineAsync(JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })).ConfigureAwait(false);
							}
						}
					}
					catch (OperationCanceledException)
					{
						return;
					}
					catch (IOException)
					{
						if (cancellationToken.IsCancellationRequested)
						{
							return;
						}
					}
				}
			}
		}

		private Task<object> OnUiAsync(Func<object> action)
		{
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			BeginInvoke(new Action(() =>
			{
				try
				{
					completion.SetResult(action());
				}
				catch (Exception ex)
				{
					completion.SetException(ex);
				}
			}));
			return completion.Task;
		}

		private object Execute(string command, JsonElement request)
		{
			var sheet = grid.CurrentWorksheet;
			switch (command)
			{
				case "health":
					return new
					{
						ready = true,
						pid = Environment.ProcessId,
						build = typeof(DiagnosticForm).Assembly.ManifestModule.ModuleVersionId,
						title = Text,
						worksheet = sheet.Name
					};
				case "state":
					return new
					{
						worksheet = sheet.Name,
						selection = sheet.SelectionRange.ToString(),
						focus = sheet.FocusPos.ToString(),
						cells = Enumerable.Range(0, 6)
							.Select(row => Enumerable.Range(0, 4).Select(col => sheet.GetCellData(row, col)).ToArray())
							.ToArray()
					};
				case "setCell":
					string address = request.GetProperty("address").GetString();
					sheet.SetCellData(address, request.GetProperty("value").GetString());
					return new { address, value = sheet.GetCellData(address) };
				case "select":
					string range = request.GetProperty("range").GetString();
					sheet.SelectionRange = new RangePosition(range);
					grid.Invalidate();
					return new { selection = sheet.SelectionRange.ToString(), focus = sheet.FocusPos.ToString() };
				case "screenshot":
					string file = Path.Combine(sessionDirectory, "grid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png");
					using (var bitmap = new Bitmap(grid.Width, grid.Height))
					{
						grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, grid.Size));
						bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
					}
					return new { path = file, width = grid.Width, height = grid.Height, surface = "WinForms ReoGridControl DrawToBitmap" };
				case "stop":
					BeginInvoke(new Action(Close));
					return new { stopping = true };
				default:
					throw new InvalidOperationException("Unsupported command: " + command);
			}
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			shutdown.Cancel();
			string descriptor = Path.Combine(sessionDirectory, "session.json");
			if (File.Exists(descriptor))
			{
				File.Delete(descriptor);
			}
			base.OnFormClosed(e);
		}
	}
}
