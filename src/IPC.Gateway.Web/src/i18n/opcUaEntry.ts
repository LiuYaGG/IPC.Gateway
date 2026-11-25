export const opcUaEntryEnglish: Record<string,string> = {
  '本地监听地址':'Local listen address',
  '对外公布端点':'Advertised endpoint',
  '客户端连接地址':'Client connection address',
  '证书附加 IP / DNS':'Additional certificate IP / DNS names',
  '每行一个 IP 或 DNS 名称；统一入口的名称自动加入证书。':'One IP address or DNS name per line. The unified endpoint name is included automatically.',
  '对外公布端点留空时使用本机地址；通过代理连接时填写统一入口，路径需与本地端点相同。修改公布地址后请重新信任更新的服务端证书。':'Leave the advertised endpoint empty for the local address. When using a proxy, enter its unified endpoint with the same path as the local endpoint. Trust the updated server certificates again after changing the address.',
  'OPC UA 监听主机必须是有效的 IP 地址或主机名。':'The OPC UA listen host must be a valid IP address or hostname.',
  'OPC UA 端点路径不能包含查询参数、片段或反斜杠。':'The OPC UA endpoint path cannot contain a query, fragment, or backslash.',
  'OPC UA 对外公布端点必须是有效的 opc.tcp 地址，路径须与本地端点相同，且不能使用通配监听地址。':'The advertised OPC UA endpoint must be a valid opc.tcp URL with the same path as the local endpoint and cannot use a wildcard host.',
  'OPC UA 证书附加名称最多 32 个，只能填写有效的 IP 地址或 DNS 名称。':'Specify at most 32 additional certificate names, using valid IP addresses or DNS names.'
}
