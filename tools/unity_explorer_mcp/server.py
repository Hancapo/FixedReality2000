from __future__ import annotations

import json
import os
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

from fastmcp import FastMCP


mcp = FastMCP(
    "Unity Explorer",
    instructions=(
        "Inspect and edit live Unity objects in Broken Reality 2000. "
        "The game must be running with Fixed Reality 2000 and UnityExplorer loaded."
    ),
)

BRIDGE_URL = os.environ.get(
    "UNITY_EXPLORER_BRIDGE_URL",
    "http://127.0.0.1:53987",
).rstrip("/")


def _request(endpoint: str, **parameters: Any) -> dict[str, Any]:
    query = {
        key: str(value).lower() if isinstance(value, bool) else str(value)
        for key, value in parameters.items()
        if value is not None and value != ""
    }
    url = f"{BRIDGE_URL}/{endpoint.lstrip('/')}"
    if query:
        url += "?" + urllib.parse.urlencode(query)

    try:
        with urllib.request.urlopen(url, timeout=12) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.URLError as error:
        raise RuntimeError(
            "The Unity runtime bridge is unavailable. Start Broken Reality 2000 "
            "and wait until BepInEx finishes loading."
        ) from error


@mcp.tool
def unity_ping() -> dict[str, Any]:
    """Check the live game, active scene, bridge, and UnityExplorer status."""
    return _request("ping")


@mcp.tool
def find_objects(
    name: str = "",
    component: str = "",
    include_inactive: bool = True,
    limit: int = 50,
) -> dict[str, Any]:
    """Find live GameObjects by partial name/path and optional component type."""
    return _request(
        "find",
        name=name,
        component=component,
        includeInactive=include_inactive,
        limit=limit,
    )


@mcp.tool
def inspect_object(
    instance_id: int = 0,
    path: str = "",
) -> dict[str, Any]:
    """Inspect components and detailed UI/RectTransform/TextMeshPro properties."""
    return _request("inspect", id=instance_id, path=path)


@mcp.tool
def get_hierarchy(
    instance_id: int = 0,
    path: str = "",
    depth: int = 2,
    max_children: int = 100,
) -> dict[str, Any]:
    """Read a live GameObject hierarchy from an instance ID or exact path."""
    return _request(
        "hierarchy",
        id=instance_id,
        path=path,
        depth=depth,
        maxChildren=max_children,
    )


@mcp.tool
def open_in_unity_explorer(
    instance_id: int = 0,
    path: str = "",
) -> dict[str, Any]:
    """Open a live GameObject in UnityExplorer's public Inspector API."""
    return _request("ue-inspect", id=instance_id, path=path)


@mcp.tool
def set_property(
    instance_id: int,
    component: str,
    property_name: str,
    value: str,
) -> dict[str, Any]:
    """Set a supported live property; Vector2 values use the form 'x,y'."""
    return _request(
        "set",
        id=instance_id,
        component=component,
        property=property_name,
        value=value,
    )


if __name__ == "__main__":
    mcp.run()
