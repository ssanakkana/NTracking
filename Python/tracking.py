import uiautomation as auto
import time

last_text = ""
while True:
    # 获取当前焦点控件
    control = auto.GetFocusedControl()
    if control:
        # 尝试获取值
        value = control.GetPattern(auto.PatternID.Value).Value
        if value and value != last_text:
            print(f"检测到新输入：{value}")
            last_text = value
    time.sleep(0.1) # 轮询间隔