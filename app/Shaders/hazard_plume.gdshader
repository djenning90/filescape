// Planets (original art): the glow rising out of a hole in a hazard world's shell (PLANET-34), the black hole's light
// escaping. A quad standing on its local Y axis (the hole's outward direction) that turns about that axis to face the
// camera; ember orange at the base fading to violet and to nothing at the top, with a slow shimmer. Tall enough to show
// over the horizon from the arrival, so you know where the danger is before you can see the edge. Additive and unlit.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 base_color : source_color = vec3(1.0, 0.5, 0.22);
uniform vec3 top_color : source_color = vec3(0.62, 0.3, 1.0);
uniform float energy = 0.6;

varying float along;   // 0 at the base, 1 at the top
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
	float width = 1.0 - across * across;
	float shimmer = 0.75 + 0.25 * sin(TIME * 1.7 + along * 9.0 + across * 3.0);
	float fade = pow(clamp(1.0 - along, 0.0, 1.0), 0.9) * smoothstep(0.0, 0.08, along);
	vec3 col = mix(base_color, top_color, smoothstep(0.0, 0.6, along));
	ALBEDO = col * width * width * fade * shimmer * energy;
}
