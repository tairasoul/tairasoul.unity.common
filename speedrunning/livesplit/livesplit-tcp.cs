using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace tairasoul.unity.common.speedrunning.livesplit;

public class LivesplitTCP : ITimer {
  private TcpClient _client;
	internal int port = 16834;
	public void Connect() {
		_client = new();
		_client.Connect("localhost", port);
	}

	async void Send(string command) {
		byte[] data = Encoding.UTF8.GetBytes(command + "\n");
		await _client.GetStream().WriteAsync(data, 0, data.Length);
		await _client.GetStream().FlushAsync();
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