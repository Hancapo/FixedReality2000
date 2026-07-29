# Unity Explorer MCP bridge

This MCP server connects Codex to the live Unity runtime bridge included in
Fixed Reality 2000. Broken Reality 2000 must be running with both BepInEx and
UnityExplorer loaded.

The bridge listens only on `127.0.0.1:53987`.

Available tools:

- `unity_ping`
- `find_objects`
- `inspect_object`
- `get_hierarchy`
- `open_in_unity_explorer`
- `set_property`
