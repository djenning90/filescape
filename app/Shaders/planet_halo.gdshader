// Planets ("Lantern Cosmos", original art): a soft additive halo sprite (a camera-facing quad) around a lantern.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(1.0, 0.8, 0.45);
uniform float energy = 0.8;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float d = length(UV - vec2(0.5)) * 2.0;
	float a = exp(-d * d * 5.0) * (1.0 - smoothstep(0.7, 1.0, d));
	ALBEDO = color * a * energy;
}
