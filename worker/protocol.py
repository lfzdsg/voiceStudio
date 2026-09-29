"""Version 1: uint32 LE length, uint8 kind, payload. Audio payload begins with int64 sample offset."""
import json
import struct
import threading

MAX_FRAME = 1_048_576

def read_exact(stream, size):
    result = bytearray()
    while len(result) < size:
        data = stream.read(size - len(result))
        if not data:
            raise EOFError("pipe closed")
        result.extend(data)
    return bytes(result)

def read_frame(stream):
    length, = struct.unpack("<I", read_exact(stream, 4))
    if not 1 <= length <= MAX_FRAME:
        raise ValueError("invalid frame size")
    data = read_exact(stream, length)
    return data[0], data[1:]

class Sender:
    def __init__(self, stream, session_id):
        self.stream, self.session_id = stream, session_id
        self.lock = threading.Lock()

    def send(self, **event):
        event["sessionId"] = self.session_id
        data = b"\0" + json.dumps(event, ensure_ascii=False).encode("utf-8")
        frame = struct.pack("<I", len(data)) + data
        with self.lock:
            view = memoryview(frame)
            while view:
                written = self.stream.write(view)
                if not written:
                    raise EOFError("pipe closed")
                view = view[written:]
            self.stream.flush()
