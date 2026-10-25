#!/usr/bin/env python3
"""Simple UDP relay server for Egypt Roblox Split-Proxy (Layer B).

Run this on a VPS OUTSIDE Egypt. It receives obfuscated UDP frames from clients
and forwards them to the original Roblox game server, relaying responses back.

Protocol (per datagram):
  Client -> Relay:  [4 bytes big-endian original_dst_ip (as 4 octets)]? 
                   Actually we use a simpler framing: the client sends the
                   original destination as "ip:port " prefix? See below.

For simplicity this relay uses a "destination in first packet" scheme:
  First datagram from a client carries: b"DEST " + host + b" " + port + b"\\n" + payload
  Subsequent datagrams from the same client (same src addr) are forwarded to
  the remembered destination.

Run:  python3 relay_server.py --listen 0.0.0.0:51820
"""
import argparse, socket, struct, threading, time

sessions = {}  # client_addr -> (dst_host, dst_port, dst_sock, last_seen)
lock = threading.Lock()
SESSION_TTL = 120

def cleanup():
    while True:
        time.sleep(30)
        now = time.time()
        with lock:
            dead = [a for a,(h,p,s,t) in sessions.items() if now-t > SESSION_TTL]
            for a in dead:
                try: sessions[a][2].close()
                except: pass
                del sessions[a]

def handle_client(data, addr, relay_sock):
    with lock:
        sess = sessions.get(addr)
    # Check for DEST header
    if data.startswith(b"DEST "):
        try:
            rest = data[5:]
            nl = rest.index(b"\n")
            header = rest[:nl].decode()
            host, port = header.rsplit(" ", 1)
            port = int(port)
            payload = rest[nl+1:]
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sock.settimeout(5)
            with lock:
                sessions[addr] = (host, port, sock, time.time())
            sess = (host, port, sock, time.time())
        except Exception as e:
            print("bad DEST header from", addr, e)
            return
    else:
        if sess is None:
            print("no session for", addr)
            return
        host, port, sock, _ = sess
        payload = data
        with lock:
            sessions[addr] = (host, port, sock, time.time())

    host, port, sock, _ = sess
    try:
        # resolve
        dst = socket.gethostbyname(host)
        sock.sendto(payload, (dst, port))
    except Exception as e:
        print("forward err", e)
        return

    # relay response back (best-effort, short wait)
    def recv_resp():
        try:
            r, _ = sock.recvfrom(65535)
            relay_sock.sendto(r, addr)
        except socket.timeout:
            pass
        except Exception as e:
            print("recv err", e)
    threading.Thread(target=recv_resp, daemon=True).start()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--listen", default="0.0.0.0:51820")
    args = ap.parse_args()
    host, port = args.listen.rsplit(":", 1)
    port = int(port)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((host, port))
    print(f"Relay listening on {host}:{port}")
    threading.Thread(target=cleanup, daemon=True).start()
    while True:
        data, addr = sock.recvfrom(65535)
        threading.Thread(target=handle_client, args=(data, addr, sock), daemon=True).start()

if __name__ == "__main__":
    main()