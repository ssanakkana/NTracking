import argparse
import json
import socket
import sys
import urllib.error
import urllib.request


def check_port(host: str, port: int, timeout: float) -> bool:
	"""Return True if TCP port is reachable."""
	with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
		sock.settimeout(timeout)
		return sock.connect_ex((host, port)) == 0


def post_json(url: str, payload: dict, timeout: float) -> dict:
	data = json.dumps(payload).encode("utf-8")
	req = urllib.request.Request(
		url=url,
		data=data,
		headers={"Content-Type": "application/json"},
		method="POST",
	)

	with urllib.request.urlopen(req, timeout=timeout) as resp:
		raw = resp.read().decode("utf-8", errors="replace")
		return json.loads(raw)


def get_json(url: str, timeout: float) -> dict:
	req = urllib.request.Request(url=url, method="GET")
	with urllib.request.urlopen(req, timeout=timeout) as resp:
		raw = resp.read().decode("utf-8", errors="replace")
		return json.loads(raw)


def extract_text(resp_json: dict) -> str:
	# OpenAI-compatible /v1/chat/completions
	choices = resp_json.get("choices")
	if isinstance(choices, list) and choices:
		first = choices[0]
		if isinstance(first, dict):
			message = first.get("message")
			if isinstance(message, dict):
				content = message.get("content")
				if isinstance(content, str):
					return content

	# Some servers may return plain text completion field
	text = resp_json.get("text")
	if isinstance(text, str):
		return text

	return ""


def discover_model(base_url: str, timeout: float) -> str:
	resp = get_json(f"{base_url}/v1/models", timeout=timeout)
	data = resp.get("data")
	if isinstance(data, list) and data:
		first = data[0]
		if isinstance(first, dict):
			model_id = first.get("id")
			if isinstance(model_id, str) and model_id:
				return model_id
	return ""


def main() -> int:
	parser = argparse.ArgumentParser(
		description="Probe localhost:11434 and run a local LLM request test."
	)
	parser.add_argument("--host", default="127.0.0.1", help="LLM host")
	parser.add_argument("--port", type=int, default=11434, help="LLM port")
	parser.add_argument(
		"--model",
		default="",
		help="Model name. Leave empty to auto-pick first model from /v1/models",
	)
	parser.add_argument(
		"--message",
		default="你好，请用一句话介绍你自己。",
		help="User message to send",
	)
	parser.add_argument(
		"--timeout", type=float, default=30.0, help="HTTP timeout seconds"
	)
	args = parser.parse_args()

	print(f"[1/3] Probing {args.host}:{args.port} ...")
	if not check_port(args.host, args.port, timeout=3.0):
		print("[FAIL] Port is not reachable.")
		print("Hint: confirm your local model service is running on 11434.")
		return 1
	print("[OK] Port is reachable.")

	base_url = f"http://{args.host}:{args.port}"
	model = args.model.strip()
	if not model:
		print("[2/4] Discovering model from /v1/models ...")
		try:
			model = discover_model(base_url, timeout=args.timeout)
		except urllib.error.HTTPError as e:
			body = e.read().decode("utf-8", errors="replace") if e.fp else ""
			print(f"[FAIL] Model discovery HTTPError: {e.code} {e.reason}")
			if body:
				print(body[:1000])
			return 2
		except urllib.error.URLError as e:
			print(f"[FAIL] Model discovery URLError: {e.reason}")
			return 3
		except json.JSONDecodeError:
			print("[FAIL] /v1/models response is not valid JSON.")
			return 4

	if not model:
		print("[FAIL] No model discovered. Please pass --model explicitly.")
		return 5

	print(f"[OK] Using model: {model}")

	url = f"{base_url}/v1/chat/completions"
	payload = {
		"model": model,
		"stream": False,
		"messages": [{"role": "user", "content": args.message}],
	}

	print(f"[3/4] Sending request to {url} ...")
	try:
		resp_json = post_json(url, payload, timeout=args.timeout)
	except urllib.error.HTTPError as e:
		body = e.read().decode("utf-8", errors="replace") if e.fp else ""
		print(f"[FAIL] HTTPError: {e.code} {e.reason}")
		if body:
			print(body[:1000])
		return 6
	except urllib.error.URLError as e:
		print(f"[FAIL] URLError: {e.reason}")
		return 7
	except TimeoutError:
		print("[FAIL] Request timed out.")
		return 8
	except json.JSONDecodeError:
		print("[FAIL] Response is not valid JSON.")
		return 9

	text = extract_text(resp_json)
	print("[4/4] Response received.")

	if text:
		print("\n=== Model Reply ===")
		print(text)
	else:
		print("\n[WARN] Could not find text field in response. Raw JSON preview:")
		print(json.dumps(resp_json, ensure_ascii=False)[:2000])

	return 0


if __name__ == "__main__":
	sys.exit(main())

