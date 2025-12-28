using System.IO.Pipes;
using System.Text;

namespace tairasoul.unity.common.speedrunning.livesplit;

class Livesplit {
	private PipeStream pipe;
	public void ConnectPipe() {
		pipe = new NamedPipeClientStream("Livesplit");
	}

	void Send(string command) {
		if (pipe == null || !pipe.IsConnected) {
			ConnectPipe();
			return;
		}
		byte[] data = Encoding.UTF8.GetBytes(command + "\r\n");
		pipe.Write(data, 0, data.Length);
	}

	public void Split() {
		Send("split");
	}

	public void StartOrSplit() {
		Send("startorsplit");
	}

	public void Start()
	{
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
}