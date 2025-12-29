namespace tairasoul.unity.common.speedrunning.livesplit;

public interface ITimer {
	public void Connect();
	public void Split();
	public void StartOrSplit();
	public void Start();
	public void Pause();
	public void Resume();
	public void Unsplit();
	public void Skip();
	public void PauseGametime();
	public void UnpauseGametime();
	public void Reset();
}