from dump_fbx import read_node, read_property
import struct

with open('BakedMesh/Cable_cable.fbx', 'rb') as f:
    f.read(27)
    while True:
        node = read_node(f)
        if node is None: break
        if node['name'] == 'Objects':
            for child in node['children']:
                if child['name'] == 'Model':
                    props = child['props']
                    name = props[1] if len(props) > 1 else ''
                    mtype = props[2] if len(props) > 2 else ''
                    print(f"Model ID={props[0]} Name='{name}' Type='{mtype}'")
                    for p in child['children']:
                        if p['name'] == 'Properties70':
                            for prop in p['children']:
                                p_vals = prop['props']
                                if p_vals[0] in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling'):
                                    print(f"  {p_vals[0]}: {p_vals[4:]}")

