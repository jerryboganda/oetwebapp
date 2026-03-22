"use client";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconBriefcase,
  IconChevronDown,
  IconChevronRight,
  IconHome,
  IconLockOpen,
} from "@tabler/icons-react";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import { IconLayoutGrid, IconServer, IconCircle } from "@tabler/icons-react";

interface TreeNodeData {
  name: string;
  children?: TreeNodeData[];
}

interface TreeNodeProps {
  node: TreeNodeData;
  level?: number;
}

interface CheckboxTreeNodeProps {
  node: TreeNodeData;
  level?: number;
  parentChecked?: boolean;
  onCheckChange?: (checked: boolean) => void;
}

const TreeViewPage = () => {
  const basicTreeData: TreeNodeData[] = [
    {
      name: "PolytronX",
      children: [
        {
          name: "assets",
          children: [
            { name: "Css" },
            { name: "Fonts" },
            { name: "Icons" },
            { name: "Images" },
            { name: "Js" },
            { name: "Scss" },
            { name: "Vendors" },
          ],
        },
        {
          name: "node modules",
        },
        {
          name: "template",
          children: [
            { name: "index.html" },
            { name: "accordian.html" },
            { name: "animation.html" },
            { name: "calender.html" },
            { name: "clipboard.html" },
          ],
        },
        {
          name: "gulpfile",
        },
        {
          name: "package.json",
        },
        {
          name: "package-json.lock",
        },
      ],
    },
  ];

  const checkboxTreeData: TreeNodeData[] = [
    {
      name: "PolytronX",
      children: [
        {
          name: "App",
          children: [
            { name: "Invoice" },
            { name: "Profile" },
            { name: "calendar" },
            { name: "faqs" },
            { name: "kanban Board" },
            { name: "timeline" },
          ],
        },
        {
          name: "Dash board",
          children: [
            { name: "Analytics" },
            { name: "Ecommerce" },
            { name: "Education" },
            { name: "Project" },
          ],
        },
        {
          name: "ui-kits",
          children: [
            { name: "alert" },
            { name: "badges" },
            { name: "buttons" },
            { name: "cards" },
            { name: "cheatsheet" },
          ],
        },
      ],
    },
  ];

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Tree View"
        title="Advance Ui"
        path={["Tree View"]}
        Icon={IconBriefcase}
      />
      <Row className="tree-view">
        <Col lg="6">
          <Card>
            <CardHeader>
              <h5>Basic Tree</h5>
            </CardHeader>
            <CardBody>
              <div className="tree-container">
                {basicTreeData.map((node, index) => (
                  <TreeNode key={index} node={node} />
                ))}
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col lg="6">
          <Card>
            <CardHeader>
              <h5>Tree With Checkbox</h5>
            </CardHeader>
            <CardBody>
              <div className="tree-container">
                {checkboxTreeData.map((node, index) => (
                  <CheckboxTreeNode key={index} node={node} />
                ))}
              </div>
            </CardBody>
          </Card>
        </Col>
      </Row>

      <style>{`
        .tree-container {
          padding-left: 0;
        }
        
        .tree-node {
          margin: 2px 0;
        }
        
        .tree-node-content {
          display: flex;
          align-items: center;
          cursor: pointer;
          padding: 6px 8px;
          border-radius: 4px;
          transition: background-color 0.2s;
        }
        
        .tree-node-content:hover {
          background-color: #f5f5f5;
        }
        
        .tree-node-icon {
          margin-right: 8px;
          display: flex;
          align-items: center;
          color: #6c757d;
        }
        
        .tree-node-chevron {
          margin-right: 4px;
          transition: transform 0.2s;
          width: 16px;
          height: 16px;
        }
        
        .tree-node-chevron.collapsed {
          transform: rotate(-90deg);
        }
        
        .tree-node-children {
          margin-left: 24px;
          padding-left: 8px;
          border-left: 1px solid #e9ecef;
          overflow: hidden;
          transition: all 0.3s ease;
        }
        
        .tree-node-children.collapsed {
          height: 0;
          opacity: 0;
        }
        
        .tree-node-name {
          font-size: 14px;
          color: #212529;
          display: flex;
          align-items: center;
          gap: 6px;
        }
        
        .tree-node-checkbox {
          margin-right: 8px;
          width: 16px;
          height: 16px;
          cursor: pointer;
          accent-color: #8c76f0;
        }
        
        .folder-icon {
          color: #8c76f0;
        }
        
        .file-icon {
          color: #8c76f0;
        }
        
        .tree-badge {
          background-color: #e9ecef;
          color: #6c757d;
          padding: 2px 6px;
          border-radius: 4px;
          font-size: 11px;
          margin-left: 8px;
        }

        .icon-PolytronX {
          color: #8c76f0;
          font-size: 16px;
        }

        .icon-app {
          color: #8c76f0;
          font-size: 16px;
        }

        .icon-dashboard {
          color: #8c76f0;
          font-size: 16px;
        }
      `}</style>
    </Container>
  );
};

const TreeNode: React.FC<TreeNodeProps> = ({ node, level = 0 }) => {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const hasChildren = node.children && node.children.length > 0;

  const toggleCollapse = () => {
    if (hasChildren) {
      setIsCollapsed(!isCollapsed);
    }
  };

  return (
    <div className="tree-node">
      <div
        className="tree-node-content"
        onClick={toggleCollapse}
        style={{ paddingLeft: `${level * 16}px` }}
      >
        {hasChildren && (
          <span
            className={`tree-node-chevron ${isCollapsed ? "collapsed" : ""}`}
          >
            {isCollapsed ? (
              <IconChevronRight size={16} />
            ) : (
              <IconChevronDown size={16} />
            )}
          </span>
        )}
        <span className="tree-node-name">{node.name}</span>
      </div>

      {hasChildren && (
        <div className={`tree-node-children ${isCollapsed ? "collapsed" : ""}`}>
          {node.children?.map((child: TreeNodeData, index: number) => (
            <TreeNode key={index} node={child} level={level + 1} />
          ))}
        </div>
      )}
    </div>
  );
};

const CheckboxTreeNode: React.FC<CheckboxTreeNodeProps> = ({
  node,
  level = 0,
  parentChecked = false,
  onCheckChange,
}) => {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [isChecked, setIsChecked] = useState(parentChecked);
  const hasChildren = node.children && node.children.length > 0;

  useEffect(() => {
    setIsChecked(parentChecked);
  }, [parentChecked]);

  const toggleCollapse = () => {
    if (hasChildren) {
      setIsCollapsed(!isCollapsed);
    }
  };

  const handleCheckboxChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const checked = e.target.checked;
    setIsChecked(checked);

    // Notify parent of change
    if (onCheckChange) {
      onCheckChange(checked);
    }
  };

  const getIcon = () => {
    if (node.name.toLowerCase() === "PolytronX") {
      return <IconLayoutGrid size={16} className="icon-PolytronX" />;
    } else if (node.name.toLowerCase() === "app") {
      return <IconServer size={16} className="icon-app" />;
    } else if (
      node.name.toLowerCase().includes("dash") ||
      node.name.includes("board")
    ) {
      return <IconHome size={16} className="icon-dashboard" />;
    } else if (node.name.toLowerCase().includes("ui-kits")) {
      return <IconLockOpen size={16} className="icon-dashboard" />;
    } else if (hasChildren) {
      return <IconServer size={16} className="icon-app" />;
    } else {
      return <IconCircle size={16} className="file-icon" />;
    }
  };

  return (
    <div className="tree-node">
      <div
        className="tree-node-content"
        onClick={toggleCollapse}
        style={{ paddingLeft: `${level * 16}px` }}
      >
        {hasChildren && (
          <span
            className={`tree-node-chevron ${isCollapsed ? "collapsed" : ""}`}
          >
            {isCollapsed ? (
              <IconChevronRight size={16} />
            ) : (
              <IconChevronDown size={16} />
            )}
          </span>
        )}

        <input
          type="checkbox"
          className="tree-node-checkbox"
          checked={isChecked}
          onChange={handleCheckboxChange}
          onClick={(e) => e.stopPropagation()}
        />
        <span className="tree-node-icon">{getIcon()}</span>
        <span className="tree-node-name">{node.name}</span>
      </div>

      {hasChildren && (
        <div className={`tree-node-children ${isCollapsed ? "collapsed" : ""}`}>
          {node.children?.map((child: TreeNodeData, index: number) => (
            <CheckboxTreeNode
              key={index}
              node={child}
              level={level + 1}
              parentChecked={isChecked}
              onCheckChange={(childChecked) => {
                if (!childChecked && isChecked) {
                  setIsChecked(false);
                  if (onCheckChange) {
                    onCheckChange(false);
                  }
                }
              }}
            />
          ))}
        </div>
      )}
    </div>
  );
};
export default TreeViewPage;
