# RYNAT 中转页

这个目录里的 `index.html` 可以直接部署到任意静态站点。

测试 URL：

```text
http://localhost:8080/?h=nas.local&s=Media&p=/Movies/demo.mp4&t=file
```

正式部署时可以通过平台路由把 `/s` 指到这个页面：

```text
https://links.example.com/s?h=nas.local&s=Media&p=/Movies/demo.mp4&t=file
```

页面不会访问 NAS，不保存状态，只把参数转成 `rynat://s?...` 并尝试唤醒客户端。
