namespace TestApplication
{
	public interface IMod
	{
		abstract IAppDialog GetDialog();
	}

	public interface IAppDialog
	{
		public void Prepare(DialogConfig config);
		public void Show();
	}
}