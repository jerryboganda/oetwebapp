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
import { IconDisc, IconHistory, IconStar, IconCode } from "@tabler/icons-react";
import classnames from "classnames";

type TabItem = {
  id: string;
  title: string;
  icon: React.ReactNode;
  content: string[];
};

const tabs: TabItem[] = [
  {
    id: "features",
    title: "Features",
    icon: <IconDisc size={20} className="pe-1 ps-1" />,
    content: [
      "The idea is to use :target pseudoclass to show tabs, use anchors with fragment identifiers to switch between them.",
      "This concept leverages modern CSS to control tab visibility without JavaScript.",
    ],
  },
  {
    id: "history",
    title: "History",
    icon: <IconHistory size={20} className="pe-1 ps-1" />,
    content: [
      "Show only the last tab.",
      "If :target matches a tab, show it and hide all following siblings.",
      "Matches a tab, show it and hide all following siblings.",
    ],
  },
  {
    id: "reviews",
    title: "Reviews",
    icon: <IconStar size={20} className="pe-1 ps-1" />,
    content: [
      "The idea is to use :target pseudoclass to show tabs, use anchors with fragment identifiers to switch between them.",
    ],
  },
];

const BackgroundTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>(tabs[0]?.id || "");

  return (
    <Col lg={6}>
      <Card className="equal-card">
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5>Background Tabs</h5>
          <a href="#" id="togglerBackgroundTabBtn">
            <IconCode className="source cursor-pointer" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <Nav tabs className="tab-primary bg-primary p-1" role="tablist">
            {tabs.map((tab) => (
              <NavItem key={tab.id}>
                <NavLink
                  className={classnames({ active: activeTab === tab.id })}
                  onClick={() => setActiveTab(tab.id)}
                  id={`${tab.id}-tab`}
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

          <TabContent activeTab={activeTab} id="bgContent" className="mt-3">
            {tabs.map((tab) => (
              <TabPane
                key={tab.id}
                tabId={tab.id}
                role="tabpanel"
                id={`${tab.id}-tab-pane`}
              >
                {tab.content.map((text, idx) => (
                  <p key={idx}>{text}</p>
                ))}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerBackgroundTabBtn">
          <pre className="mt-3">
            <code className="language-html">{`<Card className="equal-card">
  <CardHeader className="d-flex justify-content-between align-items-center">
    <h5>Background Tabs</h5>
    <a href="#tab4" aria-expanded="false" aria-controls="tab4">
      <i className="icon-code source" style={{ fontSize: 32 }}></i>
    </a>
  </CardHeader>
  <CardBody>
    <Nav tabs className="tab-primary bg-primary p-1" id="Background" role="tablist">
${tabs
  .map(
    (tab, idx) => `      <NavItem role="presentation">
        <NavLink
          ${idx === 0 ? 'className="active"' : ""}
          id="${tab.id}-tab"
          role="tab"
          aria-controls="${tab.id}-tab-pane"
          aria-selected="${idx === 0 ? "true" : "false"}"
        >
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent className="mt-3" activeTab="background" id="bgContent">
${tabs
  .map(
    (tab, idx) => `      <TabPane
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
        className="${idx === 0 ? "show active" : ""}"
      >
${tab.content.map((c) => `        <p>${c}</p>`).join("\n")}
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

export default BackgroundTabs;
