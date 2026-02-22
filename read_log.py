import sys

try:
    with open('d:/GROCERY STORE/GroceryPOS/GroceryPOS/build_q.log', 'rb') as f:
        content = f.read()
    
    # Try decoding as UTF-16 LE, then UTF-8
    try:
        text = content.decode('utf-16')
    except:
        text = content.decode('utf-8', errors='ignore')
    
    errors = [line for line in text.splitlines() if 'error' in line.lower()]
    for err in errors[:20]:
        print(err)
except Exception as e:
    print(f"Error: {e}")
