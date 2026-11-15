# Writes the Godot Engine licence, its third-party copyright list and licence texts to the file named by the first
# user argument (tools/fs.sh package runs it with the same Godot that builds the release, 2026-11-15).
extends SceneTree
func _init():
	var out := "Godot Engine licence\n====================\n\n" + Engine.get_license_text() + "\n\nThird-party components in the Godot Engine\n==========================================\n"
	for part in Engine.get_copyright_info():
		out += "\n- " + str(part["name"]) + "\n"
		for c in part["parts"]:
			out += "  files: " + str(c["files"]) + "\n  copyright: " + str(c["copyright"]) + "\n  licence: " + str(c["license"]) + "\n"
	out += "\n\nLicence texts\n=============\n"
	var lic := Engine.get_license_info()
	for k in lic.keys():
		out += "\n--- " + str(k) + " ---\n" + str(lic[k]) + "\n"
	var f := FileAccess.open(OS.get_cmdline_user_args()[0], FileAccess.WRITE)
	f.store_string(out)
	f.close()
	quit()
