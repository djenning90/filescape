// Planets (original art): a Hazard World's black-hole jet (PLANET-46, AD-59): a beam of light along the black hole's spin
// axis, out through the open sky ahead of the arrival and high into the sky, so the black hole is the first thing you see
// when you land. A quad standing on its local Y axis (the jet's direction) that turns about it to face the camera; a hot
// white-violet core in a wider blue-violet sheath, knots of light streaming outwards, fading towards the tip. Additive and
// unlit; bright enough to bloom.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 core_color : source_color = vec3(0.95, 0.88, 1.0);
uniform vec3 sheath_color : source_color = vec3(0.55, 0.35, 1.0);
uniform float energy = 1.6;
uniform float knot_speed = 0.55;   // knots travel this fraction of the jet's length per second
uniform float base_fade = 0.03;    // the beam fades in over this fraction of its length from the core

varying float along;   // 0 at the core, 1 at the tip
varying float across;  // −1 … 1 across the width

void vertex() {
	along = 1.0 - UV.y;
	across = UV.x * 2.0 - 1.0;
	vec3 cam = (inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
	vec3 to_cam = cam - vec3(0.0, VERTEX.y, 0.0);
	vec3 side = cross(vec3(0.0, 1.0, 0.0), to_cam);
	side = length(side) > 1e-4 ? normalize(side) : vec3(1.0, 0.0, 0.0);
	VERTEX = side * VERTEX.x + vec3(0.0, VERTEX.y, 0.0);
}

void fragment() {
	// The beam widens a little along its length: the core narrow, the sheath flaring.
	float spread = mix(0.35, 1.0, sqrt(along));
	float a = abs(across) / spread;
	float core = exp(-a * a * 18.0);
	float sheath = clamp(1.0 - a, 0.0, 1.0);
	sheath *= sheath;
	float knots = 0.6 + 0.4 * sin((along - TIME * knot_speed) * 40.0) * sin((along - TIME * knot_speed * 0.6) * 13.0);
	float fade = pow(clamp(1.0 - along, 0.0, 1.0), 1.3) * smoothstep(0.0, base_fade, along);
	vec3 col = core_color * core * 1.6 + sheath_color * sheath * knots * 0.8;
	ALBEDO = col * fade * energy;
}
