import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Col,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  UncontrolledCollapse,
} from "reactstrap";
import {
  IconCode,
  IconLifebuoy,
  IconKeyboardShow,
  IconFileDislike,
} from "@tabler/icons-react";
import classnames from "classnames";

let DOMPurify: any = null;
if (typeof window !== "undefined") {
  DOMPurify = require("dompurify");
}

// Define types for tabs and their content
type TabItem = {
  id: string;
  title: string;
  icon: JSX.Element;
  content: string[];
};

const tabs: TabItem[] = [
  {
    id: "description",
    title: "Description",
    icon: <IconLifebuoy size={24} className="pe-1 ps-1" />,
    content: [
      "The idea is to use <code>:target</code> pseudoclass to show tabs, use anchors with fragment identifiers to switch between them. The idea is to use pseudoclass to show tabs, use anchors with fragment identifiers to switch between them.",
    ],
  },
  {
    id: "howowrk",
    title: "How It Works",
    icon: <IconKeyboardShow size={24} className="pe-1 ps-1" />,
    content: [
      "1. Show only the last tab.",
      "2. If <code>:target</code> matches a tab, show it and hide all following siblings.",
      "3. Matches a tab, show it and hide all following siblings.",
    ],
  },
  {
    id: "drawbacks",
    title: "Drawbacks",
    icon: <IconFileDislike size={24} className="pe-1 ps-1" />,
    content: [
      "The idea is to use <code>:target</code> pseudoclass to show tabs, use anchors with fragment identifiers to switch between them. The idea is to use pseudoclass to show tabs, use anchors with fragment identifiers to switch between them.",
    ],
  },
];

const LightTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>(tabs[0]?.id || "");

  return (
    <Col lg={6}>
      <Card>
        <CardHeader className="code-header">
          <h5>Light Tabs</h5>
          <a href="#" id="togglerLightTabBtn">
            <IconCode data-source="av2" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <Nav tabs className="tab-light-primary" id="Light" role="tablist">
            {tabs.map((tab) => (
              <NavItem key={tab.id}>
                <NavLink
                  className={classnames({ active: activeTab === tab.id })}
                  onClick={() => setActiveTab(tab.id)}
                  id={`${tab.id}-tab`}
                  type="button"
                  role="tab"
                  aria-controls={`${tab.id}-tab-pane`}
                  aria-selected={activeTab === tab.id ? "true" : "false"}
                >
                  {tab.icon}
                  {tab.title}
                </NavLink>
              </NavItem>
            ))}
          </Nav>

          <TabContent activeTab={activeTab} className="mt-3" id="LightContent">
            {tabs.map((tab) => (
              <TabPane
                key={tab.id}
                tabId={tab.id}
                role="tabpanel"
                id={`${tab.id}-tab-pane`}
              >
                {tab.content.map((paragraph, idx) => {
                  const sanitized =
                    typeof window !== "undefined" && DOMPurify
                      ? DOMPurify.sanitize(paragraph)
                      : paragraph;

                  return (
                    <p
                      key={idx}
                      dangerouslySetInnerHTML={{ __html: sanitized }}
                    />
                  );
                })}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerLightTabBtn">
          <pre>
            <code className="language-html">{`<Card>
  <CardHeader>
    <h5>Light Tabs</h5>
  </CardHeader>
  <CardBody>
    <Nav tabs className="tab-light-primary" id="Light">
${tabs
  .map(
    (tab, index) => `      <NavItem>
        <NavLink
          ${index === 0 ? 'className="active"' : ""}
          id="${tab.id}-tab"
          role="tab"
        >
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent activeTab="light" className="mt-3" id="LightContent">
${tabs
  .map(
    (tab) => `      <TabPane
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
      >
${tab.content.map((text) => `        <p>${text}</p>`).join("\n")}
      </TabPane>`
  )
  .join("\n")}
    </TabContent>
  </CardBody>
</Card>`}</code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default LightTabs;
