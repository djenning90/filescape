// Planets ("Lantern Cosmos", original art): the deep indigo-violet sky. A vertical gradient, two layers of twinkling
// stars, soft nebulae concentrated along a tilted band, darker dust lanes, and a few small distant spiral galaxies.
// The radiance (cubemap) pass draws only the gradient and nebulae, so it stays cheap.
shader_type sky;

uniform vec3 top_color : source_color = vec3(0.05, 0.04, 0.16);
uniform vec3 bottom_color : source_color = vec3(0.03, 0.02, 0.09);
uniform vec3 nebula_a : source_color = vec3(0.85, 0.35, 0.75);
uniform vec3 nebula_b : source_color = vec3(0.3, 0.45, 1.0);
uniform float nebula_strength = 0.55;
uniform float star_brightness = 1.8;
uniform float twinkle_speed = 1.0;          // twinkle rate multiplier (radians per second at 1)
uniform vec3 band_normal = vec3(0.35, 0.62, 0.7);
uniform vec3 galaxy_color : source_color = vec3(1.0, 0.86, 0.95);

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.11, 0.17, 0.13));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
			mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float a = 0.5;
	float s = 0.0;
	for (int i = 0; i < 5; i++) {
		s += a * vnoise(p);
		p = p * 2.03 + vec3(1.7, 9.2, 3.1);
		a *= 0.5;
	}
	return s;
}

vec3 stars(vec3 dir, float scale, float threshold, float size, float time) {
	vec3 p = dir * scale;
	vec3 c = floor(p);
	vec3 f = fract(p) - 0.5;
	float r = hash(c);
	if (r < threshold) return vec3(0.0);
	vec3 o = (vec3(hash(c + 1.7), hash(c + 3.1), hash(c + 5.3)) - 0.5) * 0.3;
	vec3 d = f - o;
	d -= dir * dot(d, dir);
	float dist2 = dot(d, d);
	float b = (r - threshold) / (1.0 - threshold);
	b = b * b;
	float s = size * (0.6 + 0.9 * b);
	float tw = 0.6 + 0.4 * sin(time * twinkle_speed * (1.0 + 4.0 * hash(c + 9.1)) + 6.283 * hash(c + 2.2));
	float core = exp(-dist2 / (s * s));
	float dist = sqrt(dist2);
	float halo = exp(-dist / (s * 1.6)) * 0.18 * b * smoothstep(0.32, 0.12, dist); // kept inside the cell: no square edges
	vec3 tint = mix(vec3(0.72, 0.8, 1.0), vec3(1.0, 0.86, 0.72), hash(c + 4.4));
	return tint * (core + halo) * (0.35 + b) * tw;
}

vec3 galaxy(vec3 dir, vec3 centre, vec3 axis, float size, float tilt) {
	centre = normalize(centre);
	float cd = dot(dir, centre);
	if (cd < 0.98) return vec3(0.0);
	vec3 t1 = normalize(cross(centre, axis));
	vec3 t2 = cross(centre, t1);
	vec2 q = vec2(dot(dir, t1), dot(dir, t2)) / size;
	q.y /= tilt;
	float r = length(q);
	float a = atan(q.y, q.x);
	float arms = 0.55 + 0.45 * cos(2.0 * a - 6.0 * log(r + 0.06));
	float disc = exp(-r * 3.2) * arms * smoothstep(1.4, 0.2, r);
	float core = exp(-r * r * 40.0);
	return galaxy_color * (disc * 0.35 + core * 0.9);
}

void sky() {
	vec3 dir = EYEDIR;
	vec3 col = mix(bottom_color, top_color, 0.5 + 0.5 * dot(dir, normalize(vec3(0.35, 0.62, 0.7))));
	vec3 bn = normalize(band_normal);
	float band = exp(-pow(dot(dir, bn) / 0.45, 2.0));
	float n1 = fbm(dir * 2.1 + vec3(3.1, 0.4, 7.0));
	float n2 = fbm(dir * 4.3 + vec3(7.7, 2.2, 1.3));
	float neb = smoothstep(0.42, 0.85, n1 * (0.55 + 0.7 * band));
	vec3 ncol = mix(nebula_a, nebula_b, smoothstep(0.3, 0.7, n2));
	col += ncol * neb * nebula_strength * (0.6 + 0.4 * n2);
	col *= 1.0 - 0.35 * smoothstep(0.55, 0.72, n2) * band;
	if (!AT_CUBEMAP_PASS) {
		float t = TIME;
		col += stars(dir, 55.0, 0.93, 0.06, t) * star_brightness;
		col += stars(dir, 130.0, 0.86, 0.075, t * 0.7) * star_brightness * 0.4 * (0.5 + 0.9 * band);
		col += galaxy(dir, vec3(0.62, 0.55, -0.56), vec3(0.2, 1.0, 0.1), 0.045, 0.42);
		col += galaxy(dir, vec3(-0.7, 0.3, 0.65), vec3(1.0, 0.3, 0.0), 0.03, 0.6) * 0.8;
		col += galaxy(dir, vec3(0.1, 0.85, 0.52), vec3(0.0, 0.2, 1.0), 0.025, 0.3) * 0.7;
		col += galaxy(dir, vec3(-0.35, -0.2, -0.9), vec3(0.6, 1.0, 0.0), 0.035, 0.5) * 0.7;
	}
	COLOR = col;
}
