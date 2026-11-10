import struct
import zlib
import os

def read_string(f, length):
    return f.read(length).decode('utf-8', errors='replace')

def read_property(f):
    type_code = f.read(1).decode('ascii')
    if type_code == 'Y':
        return struct.unpack('<h', f.read(2))[0]
    elif type_code == 'C':
        return struct.unpack('<?', f.read(1))[0]
    elif type_code == 'I':
        return struct.unpack('<i', f.read(4))[0]
    elif type_code == 'F':
        return struct.unpack('<f', f.read(4))[0]
    elif type_code == 'D':
        return struct.unpack('<d', f.read(8))[0]
    elif type_code == 'L':
        return struct.unpack('<q', f.read(8))[0]
    elif type_code in ('R', 'S'):
        length = struct.unpack('<I', f.read(4))[0]
        data = f.read(length)
        return data.decode('utf-8', errors='replace') if type_code == 'S' else data
    elif type_code in ('f', 'd', 'l', 'i', 'b', 'c'):
        array_len, encoding, comp_len = struct.unpack('<III', f.read(12))
        data = f.read(comp_len)
        if encoding == 1:
            data = zlib.decompress(data)
        item_type = {'f':'<f', 'd':'<d', 'l':'<q', 'i':'<i', 'b':'<B', 'c':'<?'}[type_code]
        item_size = struct.calcsize(item_type)
        items = [struct.unpack(item_type, data[i:i+item_size])[0] for i in range(0, len(data), item_size)]
        return items
    else:
        raise ValueError(f"Unknown type code {type_code}")

def read_node(f, depth=0):
    start = f.tell()
    header = f.read(13)
    if len(header) < 13:
        return None
    end_offset, num_props, prop_list_len, name_len = struct.unpack('<IIIB', header)
    if end_offset == 0:
        return None  # NULL record
    name = f.read(name_len).decode('utf-8', errors='replace')
    props = [read_property(f) for _ in range(num_props)]
    children = []
    while f.tell() < end_offset:
        child = read_node(f, depth + 1)
        if child is not None:
            children.append(child)
    return {'name': name, 'props': props, 'children': children}

def print_tree(node, depth=0):
    indent = "  " * depth
    p_str = ""
    if node['props']:
        p_str = " : " + ", ".join(str(p)[:50] for p in node['props'])
    print(f"{indent}{node['name']}{p_str}")
    for c in node['children']:
        print_tree(c, depth + 1)

with open('BakedMesh/Cable_cable.fbx', 'rb') as f:
    magic = f.read(23)
    version = struct.unpack('<I', f.read(4))[0]
    print(f"Magic: {magic[:20]}, Version: {version}")
    while True:
        node = read_node(f)
        if node is None:
            break
        print_tree(node)

