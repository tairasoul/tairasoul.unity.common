using System.IO.Pipes;
using System.Text;

namespace tairasoul.unity.common.speedrunning.livesplit;

public class Livesplit : ITimer {
	private PipeStream pipe;
	public void Connect() {
		pipe = new NamedPipeClientStream(".", "Livesplit", PipeDirection.InOut, PipeOptions.Asynchronous);
	}

	async void Send(string command) {
		if (pipe == null || !pipe.IsConnected) {
			Connect();
			return;
		}
		byte[] data = Encoding.UTF8.GetBytes(command + "\n");
		await pipe.WriteAsync(data, 0, data.Length);
		await pipe.FlushAsync();
	}

	public void Split() {
		Send("split");
	}

	public void StartOrSplit() {
		Send("startorsplit");
	}

	public void Start() {
		Send("start");
	}

	public void Pause() {
		Send("pause");
	}

	public void Resume() {
		Send("resume");
	}

	public void Unsplit() {
		Send("unsplit");
	}

	public void Skip() {
		Send("skipsplit");
	}

	public void PauseGametime() {
		Send("pausegametime");
	}

	public void UnpauseGametime() {
		Send("unpausegametime");
	}

	public void Reset() {
		Send("reset");
	}
}