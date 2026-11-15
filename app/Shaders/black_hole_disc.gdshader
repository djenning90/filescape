// Planets (original art): the black hole's accretion disc at the centre of a hazard world (PLANET-34, AD-53). A flat
// annulus in the node's XZ plane; hot gold at the inner edge cooling through orange to violet at the rim, with spiral
// arms and turbulent bands that turn faster the nearer they are to the middle (angular speed ∝ r^-1.5, like an orbit).
// Additive and unlit: it is a source of light, seen through the holes in the shell.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 hot : source_color = vec3(1.0, 0.93, 0.72);
uniform vec3 warm : source_color = vec3(1.0, 0.48, 0.16);
uniform vec3 cool : source_color = vec3(0.72, 0.24, 1.0);
uniform float energy = 2.2;
uniform float outer_radius = 10.0;   // metres in the mesh's own units (the mesh's outer edge)
uniform float inner_fraction = 0.2;  // the inner edge as a fraction of the outer radius
uniform float spin = 3.2;            // radians per second at the inner edge (the pattern's turn rate)

varying vec2 disc_pos;

float hash(vec2 p) {
	p = fract(p * vec2(0.1031, 0.1030));
	p += dot(p, p.yx + 33.33);
	return fract((p.x + p.y) * p.x);
}

float vnoise(vec2 x) {
	vec2 i = floor(x);
	vec2 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void vertex() {
	disc_pos = VERTEX.xz;
}

void fragment() {
	vec2 p = disc_pos / outer_radius;
	float r = length(p);
	if (r < inner_fraction || r > 1.0) discard;
	float t = (r - inner_fraction) / (1.0 - inner_fraction); // 0 at the inner edge, 1 at the rim
	// Differential rotation: each ring of the disc turns at its own rate, so the pattern winds up into spirals.
	float w = spin * pow(inner_fraction / max(r, 0.01), 1.5);
	float a = atan(p.y, p.x) - TIME * w;
	vec2 q = vec2(cos(a), sin(a)) * r;
	float arms = 0.55 + 0.45 * sin(3.0 * a + log(r) * 7.0);
	float turb = vnoise(q * 7.0 + vec2(3.1, 1.7)) * 0.6 + vnoise(q * 19.0) * 0.4;
	float bands = 0.65 + 0.35 * sin(r * 70.0 + turb * 4.0);
	vec3 col = mix(hot, warm, smoothstep(0.0, 0.35, t));
	col = mix(col, cool, smoothstep(0.35, 1.0, t));
	float fade = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(0.65, 1.0, t));
	float b = (0.45 * arms + 0.75 * turb) * bands * fade * (1.7 - t);
	ALBEDO = col * b * energy;
}
