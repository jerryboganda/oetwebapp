"use client";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  UncontrolledCollapse,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
} from "reactstrap";
import { IconBriefcase, IconCode } from "@tabler/icons-react";
import Counter, {
  counterItems,
  simpleCounterItems,
  tabData,
  updateCounterItems,
} from "@/app/advance-ui/(count_up)/_components/UpdateCounter";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import React, { useEffect, useRef, useState } from "react";
import "prismjs/themes/prism.css";
import classnames from "classnames";

interface CountUpInstance {
  reset: () => void;
  start: () => void;
  error?: boolean;
}

const SimpleCounterSection = () => {
  const [activeTab, setActiveTab] = useState("1");
  const [counters, setCounters] = useState(
    updateCounterItems.map((item) => ({
      value: 0,
      target: item.value,
    }))
  );
  const countUpInstances = useRef<CountUpInstance[]>([]);

  const handleUpdateCounters = () => {
    // Reset all instances
    countUpInstances.current.forEach((instance) => {
      if (instance && instance.reset) instance.reset();
    });
    countUpInstances.current = [];

    // Update counters with new values
    setCounters((prevCounters) =>
      prevCounters.map((counter) => ({
        ...counter,
        value: 0,
      }))
    );

    // Start new animations after state update
    setTimeout(() => {
      setCounters((prevCounters) =>
        prevCounters.map((counter) => ({
          ...counter,
          value: counter.target,
        }))
      );
    }, 50);
  };

  useEffect(() => {
    const loadPrism = async () => {
      if (typeof window !== "undefined") {
        try {
          const Prism = await import("prismjs");
          Prism.highlightAll();
        } catch (error) {
          return;
        }
      }
    };

    loadPrism();

    return () => {
      countUpInstances.current.forEach((instance) => {
        if (instance && instance.reset) instance.reset();
      });
    };
  }, []);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Count Up"
          title="Advance Ui"
          path={["Count Up"]}
          Icon={IconBriefcase}
        />

        <SimpleCounter />
        <CustomizedCounter />
        <UpdateCounter
          counters={counters}
          handleUpdateCounters={handleUpdateCounters}
        />
        <TabbedCounters activeTab={activeTab} setActiveTab={setActiveTab} />
        <DocumentationSection />
      </Container>
    </div>
  );
};

const SimpleCounter = () => (
  <Col xs="12">
    <Card>
      <CardHeader className="code-header">
        <h5>Simple</h5>
        <a href="#" id="togglerAvLightBtn" aria-label="Toggle code example">
          <IconCode data-source="av5" className="source" size={32} />
        </a>
        <div className="text-secondary mt-2">
          Explore CountUp.js for additional examples. For more examples, visit
          the official{" "}
          <a
            href="https://inorganik.github.io/countUp.js/"
            className="text-danger text-d-underline"
            target="_blank"
            rel="noopener noreferrer"
          >
            CountUp.js
          </a>{" "}
          website.
        </div>
      </CardHeader>

      <CardBody>
        <div className="simple-counter mt-4">
          {simpleCounterItems.map((item, idx) => (
            <div className="simple" key={idx}>
              <div>
                {item.prefix && <span>{item.prefix}</span>}
                <Counter
                  className="counter"
                  tag="p"
                  value={item.value}
                  aria-live="polite"
                  aria-atomic="true"
                />
                {item.suffix && <span>{item.suffix}</span>}
              </div>
              <p>
                {item.icon}
                {item.label}
              </p>
            </div>
          ))}
        </div>

        <UncontrolledCollapse toggler="#togglerAvLightBtn" className="mt-3">
          <pre>
            <code className="language-html">{`<div class="simple-counter">
${simpleCounterItems
  .map((item) => {
    const prefix = item.prefix ? `<span>${item.prefix}</span>` : "";
    const suffix = item.suffix ? `<span>${item.suffix}</span>` : "";
    const counter = `<p class="counter">${item.value}</p>`;
    const iconChar = item.label === "Projects" ? "↓" : "↑";
    return `  <div class="simple">
    <div>${prefix}${counter}${suffix}</div>
    <p>${iconChar} ${item.label}</p>
  </div>`;
  })
  .join("\n")}
</div>`}</code>
          </pre>
        </UncontrolledCollapse>
      </CardBody>
    </Card>
  </Col>
);

const CustomizedCounter = () => (
  <Col xs="12">
    <Card>
      <CardHeader className="code-header">
        <h5>Customized</h5>
        <a href="#" id="toggleCustomized" aria-label="Toggle code example">
          <IconCode data-source="av5" className="source" size={32} />
        </a>
        <div className="text-secondary mt-2">
          You have the flexibility to modify CountUp by incorporating our
          extended Bootstrap classes.
        </div>
      </CardHeader>
      <CardBody>
        <div className="customized-counter">
          {counterItems.map((item, idx) => (
            <div className="customized text-center" key={idx}>
              <div className="icon-edit">{item.icon}</div>
              <div className="d-flex justify-content-center align-items-center">
                {item.prefix && <span>{item.prefix}</span>}
                <Counter
                  className="counter f-w-500 f-s-30 text-dark"
                  value={item.value}
                  tag={item.tag}
                  aria-live="polite"
                  aria-atomic="true"
                />
                {item.suffix && <span>{item.suffix}</span>}
              </div>
              <p>{item.description}</p>
            </div>
          ))}
        </div>

        <UncontrolledCollapse toggler="#toggleCustomized" className="mt-3">
          <pre className="av1">
            <code className="language-html">
              {`<div class="customized-counter mt-4">
${counterItems
  .map((item) => {
    const prefix = item.prefix ? `<span>${item.prefix}</span>` : "";
    const suffix = item.suffix ? `<span>${item.suffix}</span>` : "";
    const content =
      item.prefix || item.suffix
        ? `  <div class="d-flex align-items-center">
    ${prefix}
    <${item.tag} class="counter f-w-500 f-s-30 text-dark" data-count="${item.value}">0</${item.tag}>
    ${suffix}
  </div>`
        : `  <div class="counter" data-count="${item.value}">0</div>`;

    return `  <div class="customized">
    <i class="${item.iconClass}"></i>
${content}
    <p>${item.description}</p>
  </div>`;
  })
  .join("\n")}
</div>`}
            </code>
          </pre>
        </UncontrolledCollapse>
      </CardBody>
    </Card>
  </Col>
);

interface UpdateCounterProps {
  counters: Array<{ value: number; target: number }>;
  handleUpdateCounters: () => void;
}

const UpdateCounter: React.FC<UpdateCounterProps> = ({
  counters,
  handleUpdateCounters,
}) => (
  <Col xs="12">
    <div className="card">
      <div className="card-header code-header">
        <h5>Update Data</h5>
        <a href="#" id="togglerCustomizeBtn" aria-label="Toggle code example">
          <IconCode data-source="av5" className="source" size={32} />
        </a>
        <div className="text-secondary mt-2">
          Refer to CountUp.js&#39;s official documentation to understand the
          plugin plugin API. The provided example demonstrates how to reset a
          CountUp CountUp with a new value and configuration to dynamically
          update the displayed value.
        </div>
      </div>
      <div className="card-body">
        <div className="simple-counter mt-4">
          <div className="d-flex gap-3">
            {updateCounterItems.map((item, index) => (
              <div className="simple" key={index}>
                <div className="d-flex align-items-center">
                  {item.prefix && <span>{item.prefix}</span>}
                  <p
                    className="counter update-counter"
                    aria-live="polite"
                    aria-atomic="true"
                  >
                    {counters[index]?.value || 0}
                  </p>
                  {item.suffix && <span>{item.suffix}</span>}
                </div>
                <p>
                  <i className={`${item.iconClass} ${item.iconColorClass}`}></i>{" "}
                  {item.label}
                </p>
              </div>
            ))}
          </div>
          <div className="mt-3">
            <button
              className="btn btn-light-primary"
              onClick={handleUpdateCounters}
              aria-label="Update counter values"
            >
              Update Data
            </button>
          </div>
        </div>

        <UncontrolledCollapse toggler="#togglerCustomizeBtn" className="mt-3">
          <pre>
            <code className="language-html">{`<div class="simple-counter">
  ${updateCounterItems
    .map((item) => {
      const prefix = item.prefix ? `<span>${item.prefix}</span>` : "";
      const suffix = item.suffix ? `<span>${item.suffix}</span>` : "";
      const counterTag = `<p class="counter">0</p>`;
      const icon = `<i class="${item.iconClass} ${item.iconColorClass}"></i>`;

      return `  <div class="simple">
    <div>
      ${prefix}
      ${counterTag}
      ${suffix}
    </div>
    <p>${icon} ${item.label}</p>
  </div>`;
    })
    .join("\n  ")}
  <div>
    <button class="btn btn-light-primary">Update Data</button>
  </div>
</div>`}</code>
          </pre>
        </UncontrolledCollapse>
      </div>
    </div>
  </Col>
);

interface TabbedCountersProps {
  activeTab: string;
  setActiveTab: (tab: string) => void;
}

const TabbedCounters: React.FC<TabbedCountersProps> = ({
  activeTab,
  setActiveTab,
}) => (
  <Col xs="12">
    <Card>
      <CardHeader className="code-header d-flex justify-content-between align-items-start">
        <div>
          <h5>Under Tab Sections</h5>
          <div className="text-secondary mt-2">
            Experience a demo featuring CountUp, triggered only when it enters
            the viewport within a tab container.
          </div>
        </div>
        <a href="#" id="togglerCustomizeBtn1" aria-label="Toggle code example">
          <IconCode data-source="av5" className="source" size={32} />
        </a>
      </CardHeader>
      <CardBody>
        <div className="mt-3 under-tab-section">
          <Nav
            tabs
            className="nav-tabs app-tabs-primary under-tab justify-content-center"
            role="tablist"
          >
            {tabData.map((tab) => (
              <NavItem key={tab.id}>
                <NavLink
                  className={classnames("nav-link", {
                    active: activeTab === tab.id,
                  })}
                  onClick={() => setActiveTab(tab.id)}
                  role="tab"
                  aria-selected={activeTab === tab.id}
                >
                  Tab-{tab.id}
                </NavLink>
              </NavItem>
            ))}
          </Nav>

          <TabContent
            activeTab={activeTab}
            className="tab-content mt-4"
            role="tabpanel"
          >
            {tabData.map((tab) => (
              <TabPane key={tab.id} tabId={tab.id}>
                <div className="simple-counter mt-4">
                  <div className="d-flex gap-3 justify-content-center flex-wrap">
                    {tab.items.map((item, idx) => (
                      <div key={idx} className="simple text-center">
                        <div>
                          {item.prefix && <span>{item.prefix}</span>}
                          <Counter
                            value={item.value}
                            className="counter d-inline"
                            tag="p"
                            aria-live="polite"
                            aria-atomic="true"
                          />
                          {item.suffix && <span>{item.suffix}</span>}
                        </div>
                        <p>
                          {item.icon}
                          {item.label}
                        </p>
                      </div>
                    ))}
                  </div>
                </div>
              </TabPane>
            ))}
          </TabContent>
        </div>
        <UncontrolledCollapse toggler="#togglerCustomizeBtn1" className="mt-3">
          <pre>
            <code className="language-html">{`<div class="simple-counter">
  <ul class="nav nav-tabs app-tabs-primary" id="Basic" role="tablist">
${tabData
  .map((tab, index) => {
    const activeClass = index === 0 ? " active" : "";
    const selected = index === 0 ? "true" : "false";
    return `    <li class="nav-item" role="presentation">
      <button class="nav-link${activeClass}" id="${tab.id}-tab"
        type="button" role="tab"
        aria-controls="tab-${tab.id}" aria-selected="${selected}">Tab-${tab.id}</button>
    </li>`;
  })
  .join("\n")}
  </ul>
  <div class="tab-content" id="BasicContent">
${tabData
  .map((tab, index) => {
    const activeClass = index === 0 ? " show active" : "";
    return `    <div class="tab-pane fade${activeClass}" id="tab-${tab.id}"
      role="tabpanel" aria-labelledby="${tab.id}-tab" tabindex="0">
      ...
    </div>`;
  })
  .join("\n")}
  </div>
</div>`}</code>
          </pre>
        </UncontrolledCollapse>
      </CardBody>
    </Card>
  </Col>
);

const DocumentationSection = () => (
  <Col xs="12">
    <Card>
      <CardBody>
        <div className="mb-3">
          <h5 className="mb-1">Introduction</h5>
          <ul className="list-disc ms-3">
            <li className="text-secondary">
              <a href="#">
                CountUp.js is a self-reliant, lightweight JavaScript class,
                perfect for swiftly creating animations that present numerical
                data in an engaging manner. Refer to the Official Website and
                GitHub repository for additional insights.
              </a>
            </li>
          </ul>
        </div>

        <div className="mb-3">
          <h5 className="mb-1">Use</h5>
          <ul className="list-disc ms-3">
            <li className="text-secondary">
              <a href="#">
                The styling and script bundles for CountUp.js are distinct from
                our overarching bundle and need manual inclusion and initiation
                on associated pages.
              </a>
            </li>
          </ul>
          <pre>
            <code className="language-html">
              {`<script src="assets/js/countup.js"></script>`}
            </code>
          </pre>
        </div>

        <div className="mb-3">
          <h5 className="mb-1">Initiation</h5>
          <ul className="list-disc ms-3">
            <li className="text-secondary">
              <a href="#">
                Including CountUp in your project involves adding the HTML
                attribute <code></code> to the CountUp element, accompanied by
                the target counting value set using. For a comprehensive range
                of options, including loop parameters and more, please review
                the detailed choices below.
              </a>
            </li>
            <li className="text-secondary">
              <a href="#">
                You have the ability to programmatically control CountUp
                instances. To explore additional options for Smooth Scroll,
                refer to the official plugin site for comprehensive information.
              </a>
            </li>
          </ul>
        </div>

        <div>
          <h5 className="mb-1">Document Markup Guide</h5>
          <ul className="list-disc ms-3">
            <li className="text-secondary">
              <a href="#">
                Configuring specific settings in CountUp is achieved through
                HTML attributes. Below are references for each. For a full range
                of options, please review the official documentation.
              </a>
            </li>
          </ul>
        </div>
      </CardBody>
    </Card>
  </Col>
);

export default SimpleCounterSection;
