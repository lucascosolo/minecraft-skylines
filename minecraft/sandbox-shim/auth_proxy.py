"""Build-host shim, never shipped: a loopback HTTP CONNECT forwarder that adds the upstream proxy's
credentials. Loom's downloads use java.net.http.HttpClient with no Authenticator, so an authenticating
proxy answers 407; pointing Java at this forwarder instead fixes that. Prints its port, then serves
until killed. Usage: python3 auth_proxy.py http://user:pass@host:port
"""
import base64
import socket
import sys
import threading
import urllib.parse


def pipe(a, b):
    try:
        while data := a.recv(65536):
            b.sendall(data)
    except OSError:
        pass
    finally:
        for s in (a, b):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


def handle(client, up_host, up_port, auth):
    try:
        head = b""
        while b"\r\n\r\n" not in head:
            chunk = client.recv(4096)
            if not chunk:
                client.close()
                return
            head += chunk
        first, rest = head.split(b"\r\n", 1)
        upstream = socket.create_connection((up_host, up_port))
        upstream.sendall(first + b"\r\nProxy-Authorization: Basic " + auth + b"\r\n" + rest)
        threading.Thread(target=pipe, args=(upstream, client), daemon=True).start()
        pipe(client, upstream)
    except OSError:
        client.close()


def main():
    p = urllib.parse.urlsplit(sys.argv[1])
    auth = base64.b64encode(f"{urllib.parse.unquote(p.username or '')}:{urllib.parse.unquote(p.password or '')}".encode())
    srv = socket.socket()
    srv.bind(("127.0.0.1", 0))
    srv.listen(64)
    print(srv.getsockname()[1], flush=True)
    while True:
        c, _ = srv.accept()
        threading.Thread(target=handle, args=(c, p.hostname, p.port or 80, auth), daemon=True).start()


if __name__ == "__main__":
    main()
