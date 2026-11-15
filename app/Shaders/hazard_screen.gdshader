// Planets (original art): the hazard world's screen layer (PLANET-34): a warm vignette that deepens as you near a broken
// edge, and during a fall streaks of light rushing out from the centre of the view (speed lines) over a violet vignette.
shader_type canvas_item;

uniform float vignette = 0.0;                                  // 0..1: edge warning
uniform vec4 vignette_color : source_color = vec4(1.0, 0.45, 0.2, 1.0);
uniform float lines = 0.0;                                     // 0..1: speed lines (falling)
uniform vec4 line_color : source_color = vec4(0.92, 0.8, 1.0, 1.0);

float hash(float n) {
	return fract(sin(n * 12.9898) * 43758.5453);
}

void fragment() {
	vec2 c = UV - vec2(0.5);
	c.x *= SCREEN_PIXEL_SIZE.y > 0.0 ? (SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x) : 1.0;
	float r = length(c);
	float edge = smoothstep(0.3, 0.85, r);
	// Speed lines: 120 angular lanes; in about half of them a streak runs outwards, each at its own speed.
	float lanes = 120.0;
	float u = (atan(c.y, c.x) + 3.14159265) / 6.2831853 * lanes;
	float id = floor(u);
	float across = abs(fract(u) - 0.5);
	float on = step(0.5, hash(id + 3.0));
	float head = fract(hash(id + 7.0) + TIME * (0.9 + 1.6 * hash(id)));
	float rr = r * 1.4;
	float streak = smoothstep(head - 0.3, head, rr) * (1.0 - smoothstep(head, head + 0.02, rr));
	float lane = 1.0 - smoothstep(0.05, 0.3, across);
	float speed = streak * lane * on * smoothstep(0.12, 0.45, r) * lines;
	vec3 col = vignette_color.rgb * edge * vignette + line_color.rgb * (speed + edge * lines * 0.6);
	float alpha = clamp(edge * (vignette * 0.55 + lines * 0.5) + speed * 0.8, 0.0, 1.0);
	COLOR = vec4(col / max(alpha, 0.001), alpha);
}
