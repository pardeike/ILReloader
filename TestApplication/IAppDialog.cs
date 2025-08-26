namespace TestApplication
{
	public interface IAppDialog
	{
		public void Prepare(DialogConfig config);
		public void Show();
	}
}