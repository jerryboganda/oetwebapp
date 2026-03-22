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
import { IconCode, IconDisc, IconHistory, IconStar } from "@tabler/icons-react";
import classnames from "classnames";

type TabItem = {
  id: string;
  title: string;
  icon: JSX.Element;
  content: string[];
};

const tabs: TabItem[] = [
  {
    id: "b-features",
    title: "Features",
    icon: <IconDisc size={24} className="pe-1 ps-1" />,
    content: [
      "The idea is to use the :target pseudoclass to show tabs, use anchors with fragment identifiers to switch between them.",
    ],
  },
  {
    id: "b-history",
    title: "History",
    icon: <IconHistory size={24} className="pe-1 ps-1" />,
    content: [
      "Show only the last tab. If :target matches a tab, show it and hide all following siblings.",
    ],
  },
  {
    id: "b-reviews",
    title: "Reviews",
    icon: <IconStar size={24} className="pe-1 ps-1" />,
    content: [
      "Use the :target pseudoclass to show tabs, and anchors with fragment identifiers to switch between them.",
    ],
  },
];

const BottomTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>(tabs[0]?.id || "");

  return (
    <Col lg={6}>
      <Card className="">
        <CardHeader className="code-header">
          <h5>Tabs Bottom Side</h5>
          <a href="#" id="togglerVerticalBottomTabBtn">
            <IconCode className="source cursor-pointer" size={32} />
          </a>
        </CardHeader>
        <CardBody className="bottom-tab">
          <Nav tabs className="app-tabs-secondary" id="b-bg" role="tablist">
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

          <TabContent activeTab={activeTab} className="mt-3" id="b-bgContent">
            {tabs.map((tab) => (
              <TabPane
                key={tab.id}
                tabId={tab.id}
                role="tabpanel"
                id={`${tab.id}-tab-pane`}
              >
                {tab.content.map((paragraph, idx) => (
                  <p key={idx}>{paragraph}</p>
                ))}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerVerticalBottomTabBtn">
          <pre className="mt-3">
            <code className="language-html">{`<Card>
  <CardHeader>
    <h5>Tabs Bottom Side</h5>
  </CardHeader>
  <CardBody className="bottom-tab">
    <Nav tabs className="app-tabs-secondary" id="b-bg" role="tablist">
${tabs
  .map(
    (tab) => `      <NavItem key="${tab.id}">
        <NavLink
          ${activeTab === tab.id ? 'className="active"' : ""}
          onClick={() => setActiveTab("${tab.id}")}
          id="${tab.id}-tab"
          type="button"
          role="tab"
          aria-controls="${tab.id}-tab-pane"
          aria-selected="${activeTab === tab.id ? "true" : "false"}"
        >
          ${tab.icon}
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent activeTab="${activeTab}" className="mt-3" id="b-bgContent">
${tabs
  .map(
    (tab) => `      <TabPane
        key="${tab.id}"
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
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

export default BottomTabs;
