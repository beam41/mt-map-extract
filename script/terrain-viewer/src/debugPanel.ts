/** The fixed top-right checkbox panel (color-by-zoom, wireframe, ring extents - see
 * main.ts for what each toggle actually wires up to). Purely a DOM/UI concern, no
 * knowledge of tiles, rings, or Three.js. */
export interface DebugPanel {
  addToggle(labelText: string, onChange: (checked: boolean) => void): void;
}

export function createDebugPanel(): DebugPanel {
  const panel = document.createElement("div");
  Object.assign(panel.style, {
    position: "fixed", top: "8px", right: "8px", zIndex: "10",
    font: "12px monospace", color: "#cfe3ff", background: "rgba(0,0,0,0.35)",
    padding: "6px 8px", borderRadius: "4px",
  });
  document.body.appendChild(panel);

  return {
    addToggle(labelText: string, onChange: (checked: boolean) => void) {
      const row = document.createElement("label");
      Object.assign(row.style, { display: "block", cursor: "pointer" });
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.addEventListener("change", () => onChange(checkbox.checked));
      row.appendChild(checkbox);
      row.appendChild(document.createTextNode(" " + labelText));
      panel.appendChild(row);
    },
  };
}
