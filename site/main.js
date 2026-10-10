// Copy buttons
document.querySelectorAll("[data-copy]").forEach((button) => {
  button.addEventListener("click", async () => {
    const text = document.getElementById(button.dataset.copy).textContent.trim();
    try {
      await navigator.clipboard.writeText(text);
      button.textContent = "Copied";
    } catch {
      const range = document.createRange();
      range.selectNodeContents(document.getElementById(button.dataset.copy));
      getSelection().removeAllRanges();
      getSelection().addRange(range);
      button.textContent = "Press ⌘C";
    }
    setTimeout(() => (button.textContent = "Copy"), 1600);
  });
});

// Menu bar clock, macOS style: "Sat 10 Oct 14:02"
const clock = document.getElementById("clock");
function tick() {
  const now = new Date();
  const day = now.toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short" }).replace(",", "");
  const time = now.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
  clock.textContent = `${day} ${time}`;
}
tick();
setInterval(tick, 15000);

// The hero Peeko's eyes follow the pointer.
const peeko = document.getElementById("hero-peeko");
const pupils = peeko ? [...peeko.querySelectorAll(".pupil")] : [];
const calm = matchMedia("(prefers-reduced-motion: reduce)").matches;
if (pupils.length && !calm) {
  let frame = 0;
  addEventListener("pointermove", (event) => {
    cancelAnimationFrame(frame);
    frame = requestAnimationFrame(() => {
      const box = peeko.getBoundingClientRect();
      const scale = box.width / 72;
      pupils.forEach((pupil) => {
        const hx = +pupil.dataset.homeX;
        const hy = +pupil.dataset.homeY;
        const dx = event.clientX - (box.left + hx * scale);
        const dy = event.clientY - (box.top + hy * scale);
        const len = Math.hypot(dx, dy) || 1;
        const reach = Math.min(4, len / 40);
        pupil.setAttribute("cx", (hx + (dx / len) * reach).toFixed(2));
        pupil.setAttribute("cy", (hy + (dy / len) * reach).toFixed(2));
      });
    });
  }, { passive: true });
}
