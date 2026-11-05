// Places (ICON-01): a file-type badge on a tower's face (or a category sign): a dark rounded plate with the type's
// white silhouette tinted by the instance colour, sampled from FileIcons' atlas. It fades out with distance so far
// towers are not cluttered with icons, and is never lit (always readable). INSTANCE_CUSTOM.x = icon index,
// .y = number of icons in the atlas, .z = fade start (m), .w = fade end (m).
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, blend_mix;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;

varying flat float icon_index;
varying flat float icon_count;
varying float fade;

void vertex() {
	icon_index = INSTANCE_CUSTOM.x;
	icon_count = max(INSTANCE_CUSTOM.y, 1.0);
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float d = distance(world, CAMERA_POSITION_WORLD);
	fade = 1.0 - smoothstep(INSTANCE_CUSTOM.z, INSTANCE_CUSTOM.w, d);
}

void fragment() {
	vec2 uv = UV;
	// A rounded plate behind the icon.
	vec2 q = abs(uv - 0.5) - 0.38;
	float plate = 1.0 - smoothstep(0.0, 0.03, length(max(q, 0.0)) - 0.1);
	vec2 iuv = (uv - 0.5) / 0.78 + 0.5;
	float inside = step(0.0, iuv.x) * step(iuv.x, 1.0) * step(0.0, iuv.y) * step(iuv.y, 1.0);
	vec4 icon = texture(atlas, vec2((icon_index + clamp(iuv.x, 0.0, 1.0)) / icon_count, iuv.y)) * inside;
	// The silhouette is white; its inner details are dark ink (also opaque): only the white part takes the tint.
	float glyph = icon.a * smoothstep(0.3, 0.7, icon.r);
	vec3 plate_col = vec3(0.025, 0.03, 0.055);
	ALBEDO = mix(plate_col, COLOR.rgb * 1.25, glyph);
	ALPHA = max(plate, icon.a) * fade;
	if (ALPHA < 0.02) discard;
}
