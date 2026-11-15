// Planets (original art): the glow round a hazard world's black hole (PLANET-34): a camera-facing quad with a bright
// photon ring at the edge of the dark core, a flickering rim and a soft halo beyond it. Additive and unlit. Unlike
// planet_halo it keeps its node's scale (the world in another's sky is drawn small) and is drawn a little towards the
// camera, so the dark sphere in the middle never hides its own ring.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(1.0, 0.72, 0.42);
uniform vec3 rim_color : source_color = vec3(0.78, 0.38, 1.0);
uniform float energy = 1.6;
uniform float ring = 0.3;          // the photon ring's radius as a fraction of the quad's half-size
uniform float push = 1.0;          // drawn this far towards the camera, in the mesh's units (about the core's radius)

void vertex() {
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	VERTEX.z += push;
}

void fragment() {
	vec2 c = UV - vec2(0.5);
	float d = length(c) * 2.0;
	float a = atan(c.y, c.x);
	float flicker = 0.8 + 0.2 * sin(a * 7.0 + TIME * 9.0) * sin(a * 3.0 - TIME * 5.0);
	float ring_v = exp(-pow((d - ring) / 0.04, 2.0)) * flicker;
	float halo = exp(-max(d - ring, 0.0) * 4.5) * smoothstep(ring * 0.9, ring, d) * 0.45;
	vec3 col = mix(color, rim_color, smoothstep(ring, 0.9, d));
	ALBEDO = col * (ring_v * 1.8 + halo) * energy * (1.0 - smoothstep(0.8, 1.0, d));
}
