namespace TestApplication
{
	public abstract class AppDialog : IAppDialog
	{
		private DialogConfig config;

		public AppDialog()
		{
			Console.WriteLine("AppDialog constructor");
		}

		public void Prepare(DialogConfig config)
		{
			this.config = config;
		}

		public virtual void Show()
		{
			Console.WriteLine($"Showing dialog with message \"{config.message}\"");
		}
	}
}