using tairasoul.unity.common.speedrunning.livesplit;

namespace tairasoul.unity.common.speedrunning.dsl.internals;

public enum TimerOperation {
	Resume,
	Split,
	Pause,
	Start,
	StartOrSplit,
	Unsplit,
	SkipSplit,
	PauseGameTime,
	UnpauseGameTime
}

public class InternalDslOperations {
	internal Livesplit livesplit;

	public void Timer(TimerOperation operation) {
		switch (operation) {
			case TimerOperation.Resume:
				livesplit.Resume();
				break;
			case TimerOperation.Split:
				livesplit.Split();
				break;
			case TimerOperation.Pause:
				livesplit.Pause();
				break;
			case TimerOperation.Start:
				livesplit.Start();
				break;
			case TimerOperation.StartOrSplit:
				livesplit.StartOrSplit();
				break;
			case TimerOperation.Unsplit:
				livesplit.Unsplit();
				break;
			case TimerOperation.SkipSplit:
				livesplit.Skip();
				break;
			case TimerOperation.PauseGameTime:
				livesplit.PauseGametime();
				break;
			case TimerOperation.UnpauseGameTime:
				livesplit.UnpauseGametime();
				break;
		}
	}
}