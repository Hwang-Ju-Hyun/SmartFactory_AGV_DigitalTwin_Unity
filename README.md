# SmartFactory AGV Digital Twin Unity Viewer

Unity is a viewer for the Server-authoritative world. It keeps the legacy Unity
HELLO/map/replication protocol and does not connect as an ESP32 RobotProtocol client.

## Physical-demo viewer

The `NetworkManagerClient` component in `SampleScene` exposes the legacy Server
endpoint in the Inspector. Its defaults are:

- Address: `127.0.0.1` (when WSL localhost forwarding is enabled)
- Port: `6666`

The address and port remain editable in the Inspector. Use the current Windows
LAN address only when a viewer on another machine must connect through the
Windows-to-WSL forwarding rule; do not rely on an old DHCP address.

If localhost is refused on the same PC, set the Inspector address to the first
IPv4 address printed by `wsl hostname -I`, or repair the Windows port-forwarding
rule separately. Both the WSL address and the Windows Wi-Fi address can change.

Start the Server from its repository before entering Play mode:

```bash
./build/Server/AGV_Server --physical-demo
```

After Unity has connected and rendered the map and AGV 1, run FakeRobot in a
separate WSL terminal if no physical ESP32 is connected as AGV 1:

```bash
./build/Server/FakeRobot 127.0.0.1:6666 1
```

Expected concise Unity logs include connection, legacy session acceptance, map
rendering, AGV 1 creation, and throttled AGV 1 position updates. Do not connect
FakeRobot and the physical ESP32 simultaneously with AGV ID 1.

## Engineering documentation

- [전체 시스템 기술 기록](docs/SMART_FACTORY_AGV_PROJECT_RECORD.md)
- [Unity 구현 및 문제 해결 기록](docs/UNITY_ENGINEERING_RECORD.md)
